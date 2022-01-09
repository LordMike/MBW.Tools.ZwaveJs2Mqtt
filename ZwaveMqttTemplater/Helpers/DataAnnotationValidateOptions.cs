using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ZwaveMqttTemplater.Helpers;

internal class DataAnnotationValidateOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    public ValidateOptionsResult Validate(string name, TOptions options)
    {
        // Ensure options are provided to validate against
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        List<ValidationResult> validationResults = new();
        if (Validator.TryValidateObject(options, new ValidationContext(options), validationResults, true))
            return ValidateOptionsResult.Success;

        string typeName = options.GetType().Name;
        List<string> errors = new();
        foreach (ValidationResult result in validationResults)
            errors.Add($"DataAnnotation validation failed for '{typeName}' members: '{string.Join(",", result.MemberNames)}' with the error: '{result.ErrorMessage}'.");

        return ValidateOptionsResult.Fail(errors);
    }
}