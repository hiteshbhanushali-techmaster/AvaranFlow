using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AvaranFlow.Alerting.Configuration
{
    /// <summary>
    /// Represents the immutable result of validating configuration options.
    /// Captures validation errors and warnings safely.
    /// </summary>
    public sealed class ValidationResult
    {
        private static readonly IReadOnlyList<string> EmptyList = new ReadOnlyCollection<string>(new string[0]);

        public static readonly ValidationResult Valid = new ValidationResult(true, EmptyList, EmptyList);

        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public ValidationResult(
            bool isValid,
            IReadOnlyList<string>? errors = null,
            IReadOnlyList<string>? warnings = null)
        {
            IsValid = isValid;
            Errors = errors != null && errors.Count > 0
                ? new ReadOnlyCollection<string>(new List<string>(errors))
                : EmptyList;
            Warnings = warnings != null && warnings.Count > 0
                ? new ReadOnlyCollection<string>(new List<string>(warnings))
                : EmptyList;
        }

        public static ValidationResult Success(IReadOnlyList<string>? warnings = null)
        {
            return new ValidationResult(true, null, warnings);
        }

        public static ValidationResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
        {
            if (errors == null || errors.Count == 0)
                throw new ArgumentException("Errors list must not be null or empty for a failure result.", nameof(errors));

            return new ValidationResult(false, errors, warnings);
        }

        public static ValidationResult Failure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message must not be empty.", nameof(error));

            return new ValidationResult(false, new[] { error }, null);
        }

        public override string ToString()
        {
            return IsValid
                ? (Warnings.Count > 0 ? $"Valid ({Warnings.Count} warnings)" : "Valid")
                : $"Invalid ({Errors.Count} errors): {string.Join("; ", Errors)}";
        }
    }
}
