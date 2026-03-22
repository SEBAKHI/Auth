using FluentValidation;

namespace Auth.Application.Validators.Rules;

/// <summary>
/// Reusable FluentValidation rule extensions for common field types.
/// </summary>
public static class SharedValidationRules
{
    public static IRuleBuilderOptions<T, string> IsValidEmail<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");
    }

    public static IRuleBuilderOptions<T, string> IsValidFirstName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");
    }

    public static IRuleBuilderOptions<T, string> IsValidLastName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");
    }

    public static IRuleBuilderOptions<T, string?> IsValidDisplayName<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(200).WithMessage("Display name must not exceed 200 characters.");
    }

    public static IRuleBuilderOptions<T, string?> IsValidPhoneNumber<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(20).WithMessage("Phone number must not exceed 20 characters.");
    }

    public static IRuleBuilderOptions<T, string> IsRequiredPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password is required.");
    }

    public static IRuleBuilderOptions<T, string> IsValidCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(100).WithMessage("Code must not exceed 100 characters.")
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("Code must contain only alphanumeric characters, dots, hyphens, and underscores.");
    }

    public static IRuleBuilderOptions<T, string> IsValidPermissionCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(200).WithMessage("Code must not exceed 200 characters.")
            .Matches(@"^[a-z0-9:*_\-]+$").WithMessage("Permission code must contain only lowercase letters, digits, colons, hyphens, underscores, and asterisks.");
    }

    public static IRuleBuilderOptions<T, string> IsValidName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
    }

    public static IRuleBuilderOptions<T, string?> IsValidDescription<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");
    }

    public static IRuleBuilderOptions<T, int> IsValidPageNumber<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.");
    }

    public static IRuleBuilderOptions<T, int> IsValidPageSize<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100.");
    }

    public static IRuleBuilderOptions<T, string?> IsValidUrl<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(2048).WithMessage("URL must not exceed 2048 characters.");
    }

    public static IRuleBuilderOptions<T, string> IsRequiredToken<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Token is required.");
    }

    public static IRuleBuilderOptions<T, string> IsValidTotpCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Verification code is required.")
            .Matches("^[0-9]{6}$").WithMessage("Verification code must be a 6-digit number.");
    }
}
