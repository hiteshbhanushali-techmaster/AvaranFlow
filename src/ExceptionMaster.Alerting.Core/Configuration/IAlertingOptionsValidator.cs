namespace ExceptionMaster.Alerting.Configuration
{
    /// <summary>
    /// Contract for validating AlertingOptions prior to pipeline activation.
    /// Guarantees that invalid options are caught early and safely reported.
    /// </summary>
    public interface IAlertingOptionsValidator
    {
        /// <summary>
        /// Validates the specified AlertingOptions instance.
        /// </summary>
        /// <param name="options">The options to validate.</param>
        /// <returns>A ValidationResult containing validation errors and warnings.</returns>
        ValidationResult Validate(AlertingOptions options);
    }
}
