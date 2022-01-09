using System.ComponentModel.DataAnnotations;

namespace ZwaveMqttTemplater.Helpers;

[AttributeUsage(AttributeTargets.Property)]
internal class FileExistsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object value, ValidationContext validationContext)
    {
        if (value is not string asString)
            throw new Exception("Unable to use " + nameof(FileExistsAttribute) + " on this property");

        if (File.Exists(asString))
            return ValidationResult.Success;

        return new ValidationResult($"The path '{asString}' does not exist");
    }
}