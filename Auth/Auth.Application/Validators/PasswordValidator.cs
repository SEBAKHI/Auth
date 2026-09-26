using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Domain.Errors;
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
    /// <param name="property">
    /// The request property the password came from (<c>Password</c>, <c>NewPassword</c>),
    /// carried on each error so the API can point at the field that broke the rule.
    /// </param>
    /// <returns>Success or a list of validation errors.</returns>
    public ErrorOr<Success> Validate(string password, string property)
    {
        var errors = new List<Error>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(PasswordErrors.Required);
            return errors;
        }

        if (password.Length < _settings.MinimumLength)
        {
            errors.Add(PasswordErrors.TooShort(_settings.MinimumLength, property));
        }

        if (_settings.RequireUppercase && !UppercaseRegex().IsMatch(password))
        {
            errors.Add(PasswordErrors.RequiresUppercase(property));
        }

        if (_settings.RequireLowercase && !LowercaseRegex().IsMatch(password))
        {
            errors.Add(PasswordErrors.RequiresLowercase(property));
        }

        if (_settings.RequireDigit && !DigitRegex().IsMatch(password))
        {
            errors.Add(PasswordErrors.RequiresDigit(property));
        }

        if (_settings.RequireSpecialCharacter && !SpecialCharRegex().IsMatch(password))
        {
            errors.Add(PasswordErrors.RequiresSpecialCharacter(property));
        }

        // Check for common weak patterns
        if (CommonPatternsRegex().IsMatch(password))
        {
            errors.Add(PasswordErrors.CommonPattern(property));
        }

        return errors.Count > 0 ? errors : Result.Success;
    }

    // The character classes below are mirrored, byte for byte, by
    // PASSWORD_CHARACTER_CLASSES in Auth_UI/packages/api/src/password-policy.ts,
    // which drives the requirement list a person sees while typing; its test
    // reads this file and fails on any drift. They are ASCII on purpose: a
    // Latin-1 capital or an Arabic question mark satisfies neither side.
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
