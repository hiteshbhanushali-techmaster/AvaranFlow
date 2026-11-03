using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AvaranFlow.Alerting.Enums;

namespace AvaranFlow.Alerting.Escalation
{
    /// <summary>
    /// Represents an immutable escalation step / level within an escalation policy.
    /// Defines the trigger condition required to transition to this level, and the destination targets to deliver to.
    /// </summary>
    public sealed class EscalationStep
    {
        private static readonly IReadOnlyList<string> EmptyStrings = new ReadOnlyCollection<string>(new string[0]);

        public int Level { get; }
        public EscalationTriggerKind TriggerKind { get; }
        public long OccurrenceCountThreshold { get; }
        public TimeSpan ElapsedTimeThreshold { get; }
        public AlertSeverity SeverityThreshold { get; }
        public IReadOnlyList<string> TargetDestinations { get; }
        public IReadOnlyList<string> TargetGroups { get; }

        public EscalationStep(
            int level,
            EscalationTriggerKind triggerKind = EscalationTriggerKind.OccurrenceCount,
            long occurrenceCountThreshold = 1,
            TimeSpan? elapsedTimeThreshold = null,
            AlertSeverity severityThreshold = AlertSeverity.Error,
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null)
        {
            if (level < 1)
                throw new ArgumentOutOfRangeException(nameof(level), "Escalation level must be >= 1.");

            Level = level;
            TriggerKind = triggerKind;
            OccurrenceCountThreshold = occurrenceCountThreshold > 0 ? occurrenceCountThreshold : 1;
            ElapsedTimeThreshold = elapsedTimeThreshold ?? TimeSpan.Zero;
            SeverityThreshold = severityThreshold;

            TargetDestinations = targetDestinations != null
                ? new ReadOnlyCollection<string>(targetDestinations.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList())
                : EmptyStrings;

            TargetGroups = targetGroups != null
                ? new ReadOnlyCollection<string>(targetGroups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList())
                : EmptyStrings;
        }

        /// <summary>
        /// Creates an initial Level 1 baseline step.
        /// </summary>
        public static EscalationStep Initial(
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null)
        {
            return new EscalationStep(1, EscalationTriggerKind.OccurrenceCount, 1, null, AlertSeverity.Info, targetDestinations, targetGroups);
        }

        /// <summary>
        /// Creates an escalation step triggered by cumulative occurrence count.
        /// </summary>
        public static EscalationStep ForCount(
            int level,
            long countThreshold,
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null)
        {
            return new EscalationStep(level, EscalationTriggerKind.OccurrenceCount, countThreshold, null, AlertSeverity.Info, targetDestinations, targetGroups);
        }

        /// <summary>
        /// Creates an escalation step triggered by elapsed time from the initial occurrence.
        /// </summary>
        public static EscalationStep ForElapsedTime(
            int level,
            TimeSpan timeThreshold,
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null)
        {
            return new EscalationStep(level, EscalationTriggerKind.ElapsedTime, 1, timeThreshold, AlertSeverity.Info, targetDestinations, targetGroups);
        }

        /// <summary>
        /// Creates an escalation step triggered by severity.
        /// </summary>
        public static EscalationStep ForSeverity(
            int level,
            AlertSeverity severityThreshold,
            IEnumerable<string>? targetDestinations = null,
            IEnumerable<string>? targetGroups = null)
        {
            return new EscalationStep(level, EscalationTriggerKind.Severity, 1, null, severityThreshold, targetDestinations, targetGroups);
        }

        /// <summary>
        /// Evaluates whether this step's trigger threshold is met.
        /// </summary>
        public bool IsTriggered(long occurrenceCount, TimeSpan elapsedTime, AlertSeverity severity)
        {
            switch (TriggerKind)
            {
                case EscalationTriggerKind.OccurrenceCount:
                    return occurrenceCount >= OccurrenceCountThreshold;

                case EscalationTriggerKind.ElapsedTime:
                    return elapsedTime >= ElapsedTimeThreshold;

                case EscalationTriggerKind.Severity:
                    return severity >= SeverityThreshold;

                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return $"EscalationStep [Level={Level}, Trigger={TriggerKind}, Count={OccurrenceCountThreshold}, Elapsed={ElapsedTimeThreshold}, Severity={SeverityThreshold}]";
        }
    }
}
