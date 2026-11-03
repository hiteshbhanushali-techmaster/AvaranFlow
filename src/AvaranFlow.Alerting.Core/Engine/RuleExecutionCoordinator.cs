using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Alerting.Infrastructure;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Suppression;

namespace AvaranFlow.Alerting.Engine
{
    /// <summary>
    /// Coordinates the evaluation of rule-scoped escalation and suppression policies for matching alert rules.
    /// Strictly adheres to the frozen processing order: Escalation State Update -> Destination Resolution -> Suppression/Cooldown Evaluation.
    /// </summary>
    public sealed class RuleExecutionCoordinator : IRuleExecutionCoordinator
    {
        private static readonly IReadOnlyList<string> EmptyStrings = new ReadOnlyCollection<string>(new string[0]);

        private readonly BoundedSuppressionCache _suppressionCache;
        private readonly BoundedEscalationTracker _escalationTracker;
        private readonly ISuppressionPolicyRegistry _suppressionRegistry;
        private readonly IEscalationPolicyRegistry _escalationRegistry;
        private readonly ISystemClock _clock;

        public BoundedSuppressionCache SuppressionCache => _suppressionCache;
        public BoundedEscalationTracker EscalationTracker => _escalationTracker;
        public ISuppressionPolicyRegistry SuppressionRegistry => _suppressionRegistry;
        public IEscalationPolicyRegistry EscalationRegistry => _escalationRegistry;

        public RuleExecutionCoordinator(
            BoundedSuppressionCache suppressionCache,
            BoundedEscalationTracker escalationTracker,
            ISuppressionPolicyRegistry suppressionRegistry,
            IEscalationPolicyRegistry escalationRegistry,
            ISystemClock? clock = null)
        {
            _suppressionCache = suppressionCache ?? throw new ArgumentNullException(nameof(suppressionCache));
            _escalationTracker = escalationTracker ?? throw new ArgumentNullException(nameof(escalationTracker));
            _suppressionRegistry = suppressionRegistry ?? throw new ArgumentNullException(nameof(suppressionRegistry));
            _escalationRegistry = escalationRegistry ?? throw new ArgumentNullException(nameof(escalationRegistry));
            _clock = clock ?? DefaultSystemClock.Instance;
        }

        public RuleExecutionDecision Evaluate(AlertRule rule, RuleEvaluationContext context)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Step 1: Update/evaluate escalation state
            int currentLevel = 1;
            bool thresholdCrossed = false;
            long occurrenceCount = context.Count;
            DateTime? firstOccurrenceUtc = null;
            DateTime? lastOccurrenceUtc = null;
            IReadOnlyList<string> effectiveDestinations = rule.TargetDestinations;
            IReadOnlyList<string> effectiveGroups = rule.TargetGroups;

            if (!string.IsNullOrEmpty(rule.EscalationPolicyId))
            {
                if (_escalationRegistry.TryGet(rule.EscalationPolicyId!, out var escalationPolicy) && escalationPolicy != null)
                {
                    var escalationKey = new EscalationKey(rule.RuleId, escalationPolicy.PolicyId, context.ExceptionKey);
                    var escalationResult = _escalationTracker.UpdateAndEvaluate(escalationKey, escalationPolicy, context);

                    currentLevel = escalationResult.CurrentLevel;
                    thresholdCrossed = escalationResult.ThresholdCrossed;
                    occurrenceCount = escalationResult.OccurrenceCount;
                    firstOccurrenceUtc = escalationResult.FirstOccurrenceUtc;
                    lastOccurrenceUtc = escalationResult.LastOccurrenceUtc;

                    if (escalationResult.EffectiveDestinations.Count > 0 || escalationResult.EffectiveGroups.Count > 0)
                    {
                        effectiveDestinations = escalationResult.EffectiveDestinations;
                        effectiveGroups = escalationResult.EffectiveGroups;
                    }
                }
            }

            // Step 2: Evaluate suppression/cooldown
            bool isPermitted;
            string decisionReason;

            if (!string.IsNullOrEmpty(rule.SuppressionPolicyId))
            {
                if (_suppressionRegistry.TryGet(rule.SuppressionPolicyId!, out var suppressionPolicy) && suppressionPolicy != null)
                {
                    var dimensionValue = suppressionPolicy.ExtractDimensionValue(context);
                    var suppressionKey = new SuppressionKey(
                        rule.RuleId,
                        suppressionPolicy.PolicyId,
                        suppressionPolicy.Dimension.ToString(),
                        dimensionValue);

                    if (thresholdCrossed)
                    {
                        // Escalation threshold crossing BYPASSES cooldown and resets cooldown window for new level
                        _suppressionCache.ResetCooldown(suppressionKey, suppressionPolicy.CooldownPeriod);
                        isPermitted = true;
                        decisionReason = "EscalationThresholdBypass";
                    }
                    else
                    {
                        if (_suppressionCache.TryAcquireDelivery(suppressionKey, suppressionPolicy.CooldownPeriod, out var remainingCooldown))
                        {
                            isPermitted = true;
                            decisionReason = "Permitted";
                        }
                        else
                        {
                            isPermitted = false;
                            decisionReason = $"SuppressedByCooldown ({remainingCooldown.TotalSeconds:F1}s remaining)";
                        }
                    }
                }
                else
                {
                    isPermitted = true;
                    decisionReason = "SuppressionPolicyNotFound";
                }
            }
            else
            {
                isPermitted = true;
                decisionReason = "NoSuppressionConfigured";
            }

            return new RuleExecutionDecision(
                ruleId: rule.RuleId,
                isPermitted: isPermitted,
                reason: decisionReason,
                escalationLevel: currentLevel,
                thresholdCrossed: thresholdCrossed,
                occurrenceCount: occurrenceCount,
                effectiveDestinations: effectiveDestinations,
                effectiveGroups: effectiveGroups,
                firstOccurrenceUtc: firstOccurrenceUtc,
                lastOccurrenceUtc: lastOccurrenceUtc);
        }
    }
}
