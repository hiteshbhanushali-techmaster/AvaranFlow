using System;

namespace ExceptionMaster.Alerting.Escalation
{
    /// <summary>
    /// Represents an immutable point-in-time snapshot of escalation state for a specific escalation key.
    /// </summary>
    public sealed class EscalationState
    {
        public int CurrentLevel { get; }
        public DateTime FirstOccurrenceUtc { get; }
        public DateTime LastOccurrenceUtc { get; }
        public long OccurrenceCount { get; }

        public EscalationState(int currentLevel, DateTime firstOccurrenceUtc, DateTime lastOccurrenceUtc, long occurrenceCount)
        {
            CurrentLevel = currentLevel;
            FirstOccurrenceUtc = firstOccurrenceUtc;
            LastOccurrenceUtc = lastOccurrenceUtc;
            OccurrenceCount = occurrenceCount;
        }

        public override string ToString()
        {
            return $"EscalationState [Level={CurrentLevel}, Count={OccurrenceCount}, First={FirstOccurrenceUtc:u}, Last={LastOccurrenceUtc:u}]";
        }
    }
}
