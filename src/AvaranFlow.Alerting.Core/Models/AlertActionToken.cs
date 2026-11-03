using System;

namespace AvaranFlow.Alerting.Models
{
    /// <summary>
    /// Represents an immutable correlation token attached to interactive notification actions (e.g. "View Details").
    /// Allows the host application to correlate user interactions back to the original ExceptionMaster occurrence
    /// without transmitting diagnostic objects to the UI layer.
    /// </summary>
    public sealed class AlertActionToken : IEquatable<AlertActionToken>
    {
        public Guid AlertId { get; }
        public Guid OccurrenceId { get; }
        public string ExceptionKey { get; }
        public string CorrelationId { get; }
        public DateTime GeneratedUtc { get; }

        public AlertActionToken(
            Guid alertId,
            Guid occurrenceId,
            string exceptionKey,
            string correlationId,
            DateTime generatedUtc)
        {
            AlertId = alertId != Guid.Empty ? alertId : Guid.NewGuid();
            OccurrenceId = occurrenceId;
            ExceptionKey = exceptionKey ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            GeneratedUtc = generatedUtc.Kind == DateTimeKind.Utc ? generatedUtc : DateTime.SpecifyKind(generatedUtc, DateTimeKind.Utc);
        }

        /// <summary>
        /// Serializes the token into a delimiter-separated URL-safe string.
        /// Format: AlertId|OccurrenceId|ExceptionKey|CorrelationId|GeneratedUtcTicks
        /// </summary>
        public string ToPayload()
        {
            return $"{AlertId:N}|{OccurrenceId:N}|{Uri.EscapeDataString(ExceptionKey)}|{Uri.EscapeDataString(CorrelationId)}|{GeneratedUtc.Ticks}";
        }

        /// <summary>
        /// Attempts to parse an AlertActionToken from a serialized payload.
        /// Safe and never throws on malformed input.
        /// </summary>
        public static bool TryParse(string? payload, out AlertActionToken? token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(payload)) return false;

            try
            {
                var parts = payload!.Split('|');
                if (parts.Length != 5) return false;

                if (!Guid.TryParse(parts[0], out var alertId)) return false;
                if (!Guid.TryParse(parts[1], out var occurrenceId)) return false;

                string exceptionKey = Uri.UnescapeDataString(parts[2]);
                string correlationId = Uri.UnescapeDataString(parts[3]);

                if (!long.TryParse(parts[4], out var ticks)) return false;
                var generatedUtc = new DateTime(ticks, DateTimeKind.Utc);

                token = new AlertActionToken(alertId, occurrenceId, exceptionKey, correlationId, generatedUtc);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Equals(AlertActionToken? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return AlertId.Equals(other.AlertId) &&
                   OccurrenceId.Equals(other.OccurrenceId) &&
                   string.Equals(ExceptionKey, other.ExceptionKey, StringComparison.Ordinal) &&
                   string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal) &&
                   GeneratedUtc.Equals(other.GeneratedUtc);
        }

        public override bool Equals(object? obj) => Equals(obj as AlertActionToken);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = AlertId.GetHashCode();
                hash = (hash * 397) ^ OccurrenceId.GetHashCode();
                hash = (hash * 397) ^ (ExceptionKey != null ? ExceptionKey.GetHashCode() : 0);
                hash = (hash * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                hash = (hash * 397) ^ GeneratedUtc.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"AlertActionToken [AlertId={AlertId:N}, OccurrenceId={OccurrenceId:N}, Key={ExceptionKey}, CorrelationId={CorrelationId}]";
        }
    }
}
