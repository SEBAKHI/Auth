using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Validators;

/// <summary>
/// Validates passwords against configured complexity requirements.
/// </summary>
public partial class PasswordValidator
{
    private readonly PasswordSettings _settings;

    public PasswordValidator(IOptionsSnapshot<PasswordSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Validates a password against the configured requirements.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <returns>Success or a list of validation errors.</returns>
    public ErrorOr<Success> Validate(string password)
    {
        var errors = new List<Error>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(Error.Validation("Password.Required", "Validation.Password.Required"));
            return errors;
        }

        if (password.Length < _settings.MinimumLength)
        {
            errors.Add(Error.Validation(
                "Password.TooShort",
                "Validation.Password.TooShort",
                metadata: new() { ["args"] = new object[] { _settings.MinimumLength } }));
        }

        if (_settings.RequireUppercase && !UppercaseRegex().IsMatch(password))
        {
            errors.Add(Error.Validation(
                "Password.RequiresUppercase",
                "Validation.Password.RequiresUppercase"));
        }

        if (_settings.RequireLowercase && !LowercaseRegex().IsMatch(password))
        {
            errors.Add(Error.Validation(
                "Password.RequiresLowercase",
                "Validation.Password.RequiresLowercase"));
        }

        if (_settings.RequireDigit && !DigitRegex().IsMatch(password))
        {
            errors.Add(Error.Validation(
                "Password.RequiresDigit",
                "Validation.Password.RequiresDigit"));
        }

        if (_settings.RequireSpecialCharacter && !SpecialCharRegex().IsMatch(password))
        {
            errors.Add(Error.Validation(
                "Password.RequiresSpecialCharacter",
                "Validation.Password.RequiresSpecialCharacter"));
        }

        // Check for common weak patterns
        if (CommonPatternsRegex().IsMatch(password))
        {
            errors.Add(Error.Validation(
                "Password.CommonPattern",
                "Validation.Password.CommonPattern"));
        }

        return errors.Count > 0 ? errors : Result.Success;
    }

    /// <summary>
    /// Gets a user-friendly description of the password requirements.
    /// </summary>
    public string GetRequirementsDescription()
    {
        var requirements = new List<string>
        {
            $"At least {_settings.MinimumLength} characters"
        };

        if (_settings.RequireUppercase)
            requirements.Add("At least one uppercase letter");

        if (_settings.RequireLowercase)
            requirements.Add("At least one lowercase letter");

        if (_settings.RequireDigit)
            requirements.Add("At least one digit");

        if (_settings.RequireSpecialCharacter)
            requirements.Add("At least one special character");

        return string.Join(", ", requirements);
    }

    [GeneratedRegex("[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex("[a-z]")]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex("[0-9]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"[!@#$%^&*()\-_=+\[\]{}|;:'"",.<>?/\\]")]
    private static partial Regex SpecialCharRegex();

    [GeneratedRegex(@"(password|123456|qwerty|abc123|letmein|admin|welcome|monkey|dragon|master|login)", RegexOptions.IgnoreCase)]
    private static partial Regex CommonPatternsRegex();
}
