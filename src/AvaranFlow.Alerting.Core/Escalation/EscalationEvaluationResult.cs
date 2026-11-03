using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AvaranFlow.Alerting.Escalation
{
    /// <summary>
    /// Represents the immutable result of updating and evaluating escalation state for an incident.
    /// </summary>
    public sealed class EscalationEvaluationResult
    {
        private static readonly IReadOnlyList<string> EmptyStrings = new ReadOnlyCollection<string>(new string[0]);

        public int PreviousLevel { get; }
        public int CurrentLevel { get; }
        public bool ThresholdCrossed { get; }
        public long OccurrenceCount { get; }
        public DateTime FirstOccurrenceUtc { get; }
        public DateTime LastOccurrenceUtc { get; }
        public IReadOnlyList<string> EffectiveDestinations { get; }
        public IReadOnlyList<string> EffectiveGroups { get; }

        public EscalationEvaluationResult(
            int previousLevel,
            int currentLevel,
            bool thresholdCrossed,
            long occurrenceCount,
            DateTime firstOccurrenceUtc,
            DateTime lastOccurrenceUtc,
            IReadOnlyList<string>? effectiveDestinations = null,
            IReadOnlyList<string>? effectiveGroups = null)
        {
            PreviousLevel = previousLevel;
            CurrentLevel = currentLevel;
            ThresholdCrossed = thresholdCrossed;
            OccurrenceCount = occurrenceCount;
            FirstOccurrenceUtc = firstOccurrenceUtc;
            LastOccurrenceUtc = lastOccurrenceUtc;
            EffectiveDestinations = effectiveDestinations ?? EmptyStrings;
            EffectiveGroups = effectiveGroups ?? EmptyStrings;
        }

        public override string ToString()
        {
            return $"EscalationEvaluationResult [Prev={PreviousLevel}, Curr={CurrentLevel}, ThresholdCrossed={ThresholdCrossed}, Count={OccurrenceCount}]";
        }
    }
}
