using FluentValidation;

namespace Auth.Application.Validators.Rules;

/// <summary>
/// Reusable FluentValidation rule extensions for common field types.
/// Message values are resource keys resolved by the localization layer at the API edge.
/// </summary>
public static class SharedValidationRules
{
    public static IRuleBuilderOptions<T, string> IsValidEmail<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat")
            .MaximumLength(256).WithMessage("Validation.Email.MaxLength");
    }

    public static IRuleBuilderOptions<T, string> IsValidFirstName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.FirstName.Required")
            .MaximumLength(100).WithMessage("Validation.FirstName.MaxLength");
    }

    public static IRuleBuilderOptions<T, string> IsValidLastName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.LastName.Required")
            .MaximumLength(100).WithMessage("Validation.LastName.MaxLength");
    }

    public static IRuleBuilderOptions<T, string?> IsValidDisplayName<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(200).WithMessage("Validation.DisplayName.MaxLength");
    }

    public static IRuleBuilderOptions<T, string?> IsValidPhoneNumber<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(20).WithMessage("Validation.PhoneNumber.MaxLength");
    }

    public static IRuleBuilderOptions<T, string> IsRequiredPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Password.Required");
    }

    public static IRuleBuilderOptions<T, string> IsValidCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Code.Required")
            .MaximumLength(100).WithMessage("Validation.Code.MaxLength100")
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("Validation.Code.InvalidFormat");
    }

    public static IRuleBuilderOptions<T, string> IsValidPermissionCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Code.Required")
            .MaximumLength(200).WithMessage("Validation.PermissionCode.MaxLength")
            .Matches(@"^[a-z0-9:*_\-]+$").WithMessage("Validation.PermissionCode.InvalidFormat");
    }

    public static IRuleBuilderOptions<T, string> IsValidName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Name.Required")
            .MaximumLength(200).WithMessage("Validation.Name.MaxLength");
    }

    public static IRuleBuilderOptions<T, string?> IsValidDescription<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(500).WithMessage("Validation.Description.MaxLength");
    }

    public static IRuleBuilderOptions<T, int> IsValidPageNumber<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .GreaterThanOrEqualTo(1).WithMessage("Validation.PageNumber.Min");
    }

    public static IRuleBuilderOptions<T, int> IsValidPageSize<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .InclusiveBetween(1, 100).WithMessage("Validation.PageSize.Range");
    }

    /// <summary>
    /// The sort field must be null (server default order) or one of the
    /// endpoint's allow-listed field names (case-insensitive). The allow-list is
    /// what keeps client input away from SQL ORDER BY clauses.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsValidSortField<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        IReadOnlyCollection<string> allowedFields)
    {
        return ruleBuilder
            .Must(field => field is null ||
                allowedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Validation.SortBy.NotAllowed");
    }

    public static IRuleBuilderOptions<T, string?> IsValidUrl<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(2048).WithMessage("Validation.Url.MaxLength");
    }

    public static IRuleBuilderOptions<T, string> IsRequiredToken<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.Token.Required");
    }

    public static IRuleBuilderOptions<T, string> IsValidTotpCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Validation.TotpCode.Required")
            .Matches("^[0-9]{6}$").WithMessage("Validation.TotpCode.InvalidFormat");
    }
}
