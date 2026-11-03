using System;
using System.Collections.Generic;
using AvaranFlow.Alerting.Destinations;
using AvaranFlow.Alerting.Escalation;
using AvaranFlow.Alerting.Infrastructure;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Alerting.Severity;
using AvaranFlow.Alerting.Suppression;

namespace AvaranFlow.Alerting.Configuration
{
    /// <summary>
    /// Fluent configuration builder for configuring Alerting options, rules, destinations, groups,
    /// fallback chains, suppression policies, and escalation policies.
    /// </summary>
    public sealed class AlertingOptionsBuilder
    {
        private readonly List<AlertRule> _rules = new List<AlertRule>();
        private readonly List<IAlertDestination> _destinations = new List<IAlertDestination>();
        private readonly List<DestinationGroup> _groups = new List<DestinationGroup>();
        private readonly List<FallbackChain> _fallbackChains = new List<FallbackChain>();
        private readonly List<ISuppressionPolicy> _suppressionPolicies = new List<ISuppressionPolicy>();
        private readonly List<IEscalationPolicy> _escalationPolicies = new List<IEscalationPolicy>();

        public AlertingOptions Options { get; }
        public ISeverityResolver? SeverityResolver { get; private set; }
        public ISystemClock? Clock { get; private set; }

        public IReadOnlyList<AlertRule> Rules => _rules;
        public IReadOnlyList<IAlertDestination> Destinations => _destinations;
        public IReadOnlyList<DestinationGroup> Groups => _groups;
        public IReadOnlyList<FallbackChain> FallbackChains => _fallbackChains;
        public IReadOnlyList<ISuppressionPolicy> SuppressionPolicies => _suppressionPolicies;
        public IReadOnlyList<IEscalationPolicy> EscalationPolicies => _escalationPolicies;

        public AlertingOptionsBuilder(AlertingOptions? initialOptions = null)
        {
            Options = initialOptions?.Clone() ?? new AlertingOptions();
        }

        public AlertingOptionsBuilder ConfigureOptions(Action<AlertingOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            configure(Options);
            return this;
        }

        public AlertingOptionsBuilder WithBoundedQueue(int capacity, AvaranFlow.Alerting.Enums.QueueOverflowPolicy overflowPolicy = AvaranFlow.Alerting.Enums.QueueOverflowPolicy.DropNewest, TimeSpan? blockTimeout = null)
        {
            Options.MaxQueueCapacity = capacity;
            Options.OverflowPolicy = overflowPolicy;
            if (blockTimeout.HasValue) Options.EnqueueBlockTimeout = blockTimeout.Value;
            return this;
        }

        public AlertingOptionsBuilder WithConcurrency(int workerConcurrency, int globalMaxDestinationConcurrency)
        {
            Options.WorkerConcurrency = workerConcurrency;
            Options.GlobalMaxDestinationConcurrency = globalMaxDestinationConcurrency;
            return this;
        }

        public AlertingOptionsBuilder WithDrainTimeouts(TimeSpan reconfigureDrainTimeout, TimeSpan shutdownDrainTimeout)
        {
            Options.ReconfigureDrainTimeout = reconfigureDrainTimeout;
            Options.ShutdownTimeout = shutdownDrainTimeout;
            return this;
        }

        public AlertingOptionsBuilder AddRule(AlertRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
            return this;
        }

        public AlertingOptionsBuilder AddRules(IEnumerable<AlertRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            foreach (var r in rules)
            {
                if (r != null) _rules.Add(r);
            }
            return this;
        }

        public AlertingOptionsBuilder RegisterDestination(IAlertDestination destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            _destinations.Add(destination);
            return this;
        }

        public AlertingOptionsBuilder RegisterGroup(DestinationGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            _groups.Add(group);
            return this;
        }

        public AlertingOptionsBuilder RegisterFallbackChain(FallbackChain chain)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            _fallbackChains.Add(chain);
            return this;
        }

        public AlertingOptionsBuilder RegisterSuppressionPolicy(ISuppressionPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _suppressionPolicies.Add(policy);
            return this;
        }

        public AlertingOptionsBuilder AddSuppressionPolicy(ISuppressionPolicy policy) => RegisterSuppressionPolicy(policy);

        public AlertingOptionsBuilder RegisterEscalationPolicy(IEscalationPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _escalationPolicies.Add(policy);
            return this;
        }

        public AlertingOptionsBuilder AddEscalationPolicy(IEscalationPolicy policy) => RegisterEscalationPolicy(policy);
        public AlertingOptionsBuilder AddDestination(IAlertDestination destination) => RegisterDestination(destination);
        public AlertingOptionsBuilder AddGroup(DestinationGroup group) => RegisterGroup(group);
        public AlertingOptionsBuilder AddFallbackChain(FallbackChain chain) => RegisterFallbackChain(chain);

        public AlertingOptionsBuilder SetSeverityResolver(ISeverityResolver resolver)
        {
            SeverityResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            return this;
        }

        public AlertingOptionsBuilder SetClock(ISystemClock clock)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            return this;
        }
    }
}
