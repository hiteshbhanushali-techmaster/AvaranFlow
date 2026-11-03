using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AvaranFlow.Alerting.Engine
{
    /// <summary>
    /// Represents the immutable decision regarding whether an alert for a matching rule should be delivered,
    /// which escalation level it has reached, and which effective destinations and groups should be targeted.
    /// </summary>
    public sealed class RuleExecutionDecision
    {
        private static readonly IReadOnlyList<string> EmptyStrings = new ReadOnlyCollection<string>(new string[0]);

        public string RuleId { get; }
        public bool IsPermitted { get; }
        public string Reason { get; }
        public int EscalationLevel { get; }
        public bool ThresholdCrossed { get; }
        public long OccurrenceCount { get; }
        public IReadOnlyList<string> EffectiveDestinations { get; }
        public IReadOnlyList<string> EffectiveGroups { get; }
        public DateTime? FirstOccurrenceUtc { get; }
        public DateTime? LastOccurrenceUtc { get; }

        public RuleExecutionDecision(
            string ruleId,
            bool isPermitted,
            string reason,
            int escalationLevel,
            bool thresholdCrossed,
            long occurrenceCount,
            IReadOnlyList<string>? effectiveDestinations = null,
            IReadOnlyList<string>? effectiveGroups = null,
            DateTime? firstOccurrenceUtc = null,
            DateTime? lastOccurrenceUtc = null)
        {
            RuleId = ruleId ?? string.Empty;
            IsPermitted = isPermitted;
            Reason = reason ?? string.Empty;
            EscalationLevel = escalationLevel;
            ThresholdCrossed = thresholdCrossed;
            OccurrenceCount = occurrenceCount;
            EffectiveDestinations = effectiveDestinations ?? EmptyStrings;
            EffectiveGroups = effectiveGroups ?? EmptyStrings;
            FirstOccurrenceUtc = firstOccurrenceUtc;
            LastOccurrenceUtc = lastOccurrenceUtc;
        }

        public override string ToString()
        {
            return $"RuleExecutionDecision [Rule={RuleId}, Permitted={IsPermitted}, Level={EscalationLevel}, Reason={Reason}, Dests={EffectiveDestinations.Count}, Groups={EffectiveGroups.Count}]";
        }
    }
}
