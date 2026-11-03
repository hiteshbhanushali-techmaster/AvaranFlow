using System;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Core;

namespace AvaranFlow.Alerting.Severity
{
    /// <summary>
    /// Implements the deterministic 4-step severity resolution precedence:
    /// 1. context.IsFatal == true -> AlertSeverity.Critical (absolute; cannot be downgraded).
    /// 2. Otherwise, explicit validated Alerting severity (if provided).
    /// 3. Otherwise, valid Exception.Data["Severity"] (parsed case-insensitively or numeric).
    /// 4. Otherwise, configured default severity (AlertSeverity.Error by default).
    /// </summary>
    public sealed class DefaultSeverityResolver : ISeverityResolver
    {
        public static readonly DefaultSeverityResolver Instance = new DefaultSeverityResolver();

        public AlertSeverity Resolve(
            ExceptionContext? context,
            AlertSeverity? explicitSeverity = null,
            AlertSeverity? defaultSeverity = null)
        {
            var fallback = defaultSeverity ?? AlertSeverity.Error;
            if (context == null)
            {
                return explicitSeverity ?? fallback;
            }

            // Step 1: Fatal Guard (Absolute; cannot be downgraded)
            if (context.IsFatal)
            {
                return AlertSeverity.Critical;
            }

            // Step 2: Explicit validated Alerting severity
            if (explicitSeverity.HasValue && Enum.IsDefined(typeof(AlertSeverity), explicitSeverity.Value))
            {
                return explicitSeverity.Value;
            }

            // Step 3: Exception.Data["Severity"] Inspection
            if (context.Exception?.Data != null && TryExtractSeverityFromData(context.Exception.Data, out var dataSeverity))
            {
                return dataSeverity;
            }

            // Step 4: Configured default fallback
            return fallback;
        }

        private static bool TryExtractSeverityFromData(System.Collections.IDictionary data, out AlertSeverity severity)
        {
            severity = AlertSeverity.Error;
            if (data == null) return false;

            try
            {
                if (!data.Contains("Severity")) return false;

                var rawValue = data["Severity"];
                if (rawValue == null) return false;

                if (rawValue is AlertSeverity enumVal && Enum.IsDefined(typeof(AlertSeverity), enumVal))
                {
                    severity = enumVal;
                    return true;
                }

                if (rawValue is int intVal && Enum.IsDefined(typeof(AlertSeverity), intVal))
                {
                    severity = (AlertSeverity)intVal;
                    return true;
                }

                var strVal = rawValue.ToString()?.Trim();
                if (string.IsNullOrEmpty(strVal)) return false;

                if (Enum.TryParse<AlertSeverity>(strVal, ignoreCase: true, out var parsedEnum) &&
                    Enum.IsDefined(typeof(AlertSeverity), parsedEnum))
                {
                    severity = parsedEnum;
                    return true;
                }

                if (int.TryParse(strVal, out var parsedInt) && Enum.IsDefined(typeof(AlertSeverity), parsedInt))
                {
                    severity = (AlertSeverity)parsedInt;
                    return true;
                }
            }
            catch
            {
                // Safety containment: never throw on malformed dictionary entry
            }

            return false;
        }
    }
}
