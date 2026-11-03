using System;
using System.Collections.Generic;
using System.Globalization;
using AvaranFlow.Alerting.Enums;

namespace AvaranFlow.Alerting.Rules
{
    /// <summary>
    /// Leaf AST condition that evaluates a target RuleField against an expected value using a PredicateOperator.
    /// Strictly internal, sandboxed, deterministic, and null-safe.
    /// </summary>
    internal sealed class FieldPredicateCondition : RuleCondition
    {
        public RuleField TargetField { get; }
        public string? MetadataKey { get; }
        public PredicateOperator Operator { get; }
        public object? ExpectedValue { get; }
        public StringComparison StringComparison { get; }

        public FieldPredicateCondition(
            RuleField field,
            PredicateOperator op,
            object? expectedValue = null,
            string? metadataKey = null,
            StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            TargetField = field;
            Operator = op;
            ExpectedValue = expectedValue;
            MetadataKey = metadataKey;
            StringComparison = stringComparison;
        }

        internal override bool Evaluate(RuleEvaluationContext context)
        {
            if (context == null) return false;

            try
            {
                // Handle Exists operator first
                if (Operator == PredicateOperator.Exists)
                {
                    return EvaluateExists(context);
                }

                // Extract target field value
                switch (TargetField)
                {
                    case RuleField.Severity:
                        return EvaluateSeverity(context.Severity);

                    case RuleField.IsFatal:
                        return EvaluateBoolean(context.IsFatal);

                    case RuleField.Count:
                        return EvaluateIntegral(context.Count);

                    case RuleField.ProcessId:
                        return EvaluateIntegral(context.ProcessId);

                    case RuleField.ThreadId:
                        return EvaluateIntegral(context.ThreadId);

                    case RuleField.FirstSeenUtc:
                        return EvaluateDateTime(context.FirstSeenUtc);

                    case RuleField.LastSeenUtc:
                        return EvaluateDateTime(context.LastSeenUtc);

                    case RuleField.Metadata:
                        return EvaluateMetadataString(context);

                    case RuleField.ExceptionType:
                        return EvaluateString(context.ExceptionType);

                    case RuleField.ExceptionKey:
                        return EvaluateString(context.ExceptionKey);

                    case RuleField.Category:
                        return EvaluateString(context.Category);

                    case RuleField.Source:
                        return EvaluateString(context.Source);

                    case RuleField.EnvironmentName:
                        return EvaluateString(context.EnvironmentName);

                    case RuleField.MachineName:
                        return EvaluateString(context.MachineName);

                    case RuleField.ProcessName:
                        return EvaluateString(context.ProcessName);

                    case RuleField.AppVersion:
                        return EvaluateString(context.AppVersion);

                    case RuleField.CorrelationId:
                        return EvaluateString(context.CorrelationId);

                    case RuleField.Caller:
                        return EvaluateString(context.Caller);

                    case RuleField.Message:
                        return EvaluateString(context.Message);

                    default:
                        return false;
                }
            }
            catch
            {
                // Absolute failure containment: never allow unexpected comparison error to throw
                return false;
            }
        }

        private bool EvaluateExists(RuleEvaluationContext context)
        {
            if (TargetField == RuleField.Metadata)
            {
                if (string.IsNullOrEmpty(MetadataKey)) return false;
                return context.Metadata.TryGetValue(MetadataKey!, out var val) && !string.IsNullOrEmpty(val);
            }

            // For standard fields, check if non-empty / valid
            switch (TargetField)
            {
                case RuleField.ExceptionType:
                    return !string.IsNullOrEmpty(context.ExceptionType);
                case RuleField.ExceptionKey:
                    return !string.IsNullOrEmpty(context.ExceptionKey);
                case RuleField.Category:
                    return !string.IsNullOrEmpty(context.Category);
                case RuleField.CorrelationId:
                    return !string.IsNullOrEmpty(context.CorrelationId);
                case RuleField.Source:
                    return !string.IsNullOrEmpty(context.Source);
                case RuleField.Message:
                    return !string.IsNullOrEmpty(context.Message);
                default:
                    return true;
            }
        }

        private bool EvaluateString(string? actualValue)
        {
            string actual = actualValue ?? string.Empty;
            string expected = ExpectedValue?.ToString() ?? string.Empty;

            switch (Operator)
            {
                case PredicateOperator.Equals:
                    return string.Equals(actual, expected, StringComparison);

                case PredicateOperator.NotEquals:
                    return !string.Equals(actual, expected, StringComparison);

                case PredicateOperator.Contains:
                    return actual.IndexOf(expected, StringComparison) >= 0;

                case PredicateOperator.StartsWith:
                    return actual.StartsWith(expected, StringComparison);

                case PredicateOperator.EndsWith:
                    return actual.EndsWith(expected, StringComparison);

                case PredicateOperator.GreaterThan:
                case PredicateOperator.GreaterThanOrEqual:
                case PredicateOperator.LessThan:
                case PredicateOperator.LessThanOrEqual:
                    // If both can be parsed as integers, exact integral comparison
                    if (long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intA) &&
                        long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intB))
                    {
                        return CompareIntegers(intA, intB);
                    }
                    // If both can be parsed as decimals/doubles, exact floating point comparison without epsilon
                    if (double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var numA) &&
                        double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var numB))
                    {
                        return CompareExactDoubles(numA, numB);
                    }
                    return CompareStringsRelational(actual, expected);

                default:
                    return false;
            }
        }

        private bool EvaluateMetadataString(RuleEvaluationContext context)
        {
            if (string.IsNullOrEmpty(MetadataKey)) return false;

            if (!context.Metadata.TryGetValue(MetadataKey!, out var val))
            {
                // Key not present in metadata
                return Operator == PredicateOperator.NotEquals && ExpectedValue != null;
            }

            return EvaluateString(val);
        }

        private bool EvaluateSeverity(AlertSeverity actual)
        {
            if (!TryExtractExpectedSeverity(out var expected))
            {
                return Operator == PredicateOperator.NotEquals;
            }

            int cmp = actual.CompareTo(expected);
            switch (Operator)
            {
                case PredicateOperator.Equals:
                    return cmp == 0;
                case PredicateOperator.NotEquals:
                    return cmp != 0;
                case PredicateOperator.GreaterThan:
                    return cmp > 0;
                case PredicateOperator.GreaterThanOrEqual:
                    return cmp >= 0;
                case PredicateOperator.LessThan:
                    return cmp < 0;
                case PredicateOperator.LessThanOrEqual:
                    return cmp <= 0;
                default:
                    return false;
            }
        }

        private bool TryExtractExpectedSeverity(out AlertSeverity severity)
        {
            severity = AlertSeverity.Error;
            if (ExpectedValue == null) return false;

            if (ExpectedValue is AlertSeverity enumVal)
            {
                severity = enumVal;
                return true;
            }

            if (ExpectedValue is int intVal && Enum.IsDefined(typeof(AlertSeverity), intVal))
            {
                severity = (AlertSeverity)intVal;
                return true;
            }

            string strVal = ExpectedValue.ToString()?.Trim() ?? string.Empty;
            return Enum.TryParse(strVal, ignoreCase: true, out severity);
        }

        private bool EvaluateBoolean(bool actual)
        {
            bool expected = false;
            if (ExpectedValue is bool b)
            {
                expected = b;
            }
            else if (ExpectedValue != null && bool.TryParse(ExpectedValue.ToString(), out var parsedBool))
            {
                expected = parsedBool;
            }
            else
            {
                return Operator == PredicateOperator.NotEquals;
            }

            switch (Operator)
            {
                case PredicateOperator.Equals:
                    return actual == expected;
                case PredicateOperator.NotEquals:
                    return actual != expected;
                default:
                    return false;
            }
        }

        private bool EvaluateIntegral(long actual)
        {
            if (!TryExtractExpectedInteger(out long expected))
            {
                return Operator == PredicateOperator.NotEquals;
            }

            switch (Operator)
            {
                case PredicateOperator.Equals:
                    return actual == expected;
                case PredicateOperator.NotEquals:
                    return actual != expected;
                default:
                    return CompareIntegers(actual, expected);
            }
        }

        private bool TryExtractExpectedInteger(out long number)
        {
            number = 0;
            if (ExpectedValue == null) return false;

            if (ExpectedValue is long l) { number = l; return true; }
            if (ExpectedValue is int i) { number = i; return true; }
            if (ExpectedValue is short s) { number = s; return true; }
            if (ExpectedValue is byte b) { number = b; return true; }

            string str = ExpectedValue.ToString()?.Trim() ?? string.Empty;
            return long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        private bool CompareIntegers(long actual, long expected)
        {
            switch (Operator)
            {
                case PredicateOperator.GreaterThan:
                    return actual > expected;
                case PredicateOperator.GreaterThanOrEqual:
                    return actual >= expected;
                case PredicateOperator.LessThan:
                    return actual < expected;
                case PredicateOperator.LessThanOrEqual:
                    return actual <= expected;
                default:
                    return false;
            }
        }

        private bool CompareExactDoubles(double actual, double expected)
        {
            switch (Operator)
            {
                case PredicateOperator.GreaterThan:
                    return actual > expected;
                case PredicateOperator.GreaterThanOrEqual:
                    return actual >= expected;
                case PredicateOperator.LessThan:
                    return actual < expected;
                case PredicateOperator.LessThanOrEqual:
                    return actual <= expected;
                default:
                    return false;
            }
        }

        private bool CompareStringsRelational(string actual, string expected)
        {
            int cmp = string.Compare(actual, expected, StringComparison);
            switch (Operator)
            {
                case PredicateOperator.GreaterThan:
                    return cmp > 0;
                case PredicateOperator.GreaterThanOrEqual:
                    return cmp >= 0;
                case PredicateOperator.LessThan:
                    return cmp < 0;
                case PredicateOperator.LessThanOrEqual:
                    return cmp <= 0;
                default:
                    return false;
            }
        }

        private bool EvaluateDateTime(DateTime actual)
        {
            DateTime expected;
            if (ExpectedValue is DateTime dt)
            {
                expected = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            else if (ExpectedValue is DateTimeOffset dto)
            {
                expected = dto.UtcDateTime;
            }
            else if (ExpectedValue != null && DateTime.TryParse(ExpectedValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDt))
            {
                expected = parsedDt;
            }
            else
            {
                return Operator == PredicateOperator.NotEquals;
            }

            int cmp = actual.CompareTo(expected);
            switch (Operator)
            {
                case PredicateOperator.Equals:
                    return cmp == 0;
                case PredicateOperator.NotEquals:
                    return cmp != 0;
                case PredicateOperator.GreaterThan:
                    return cmp > 0;
                case PredicateOperator.GreaterThanOrEqual:
                    return cmp >= 0;
                case PredicateOperator.LessThan:
                    return cmp < 0;
                case PredicateOperator.LessThanOrEqual:
                    return cmp <= 0;
                default:
                    return false;
            }
        }

        internal override void Validate(List<string> errors)
        {
            if (!Enum.IsDefined(typeof(RuleField), TargetField))
            {
                errors.Add($"Field ({(int)TargetField}) is not a defined RuleField.");
            }

            if (!Enum.IsDefined(typeof(PredicateOperator), Operator))
            {
                errors.Add($"Operator ({(int)Operator}) is not a defined PredicateOperator.");
            }

            if (TargetField == RuleField.Metadata && string.IsNullOrWhiteSpace(MetadataKey))
            {
                errors.Add("MetadataKey must be provided and non-empty when Field is RuleField.Metadata.");
            }

            if (Operator != PredicateOperator.Exists && ExpectedValue == null && Operator != PredicateOperator.Equals && Operator != PredicateOperator.NotEquals)
            {
                errors.Add($"ExpectedValue must not be null for operator {Operator}.");
            }
        }

        public override string ToString()
        {
            string fieldName = TargetField == RuleField.Metadata ? $"Metadata[\"{MetadataKey}\"]" : TargetField.ToString();
            return Operator == PredicateOperator.Exists
                ? $"{fieldName} Exists"
                : $"{fieldName} {Operator} \"{ExpectedValue}\"";
        }
    }
}
