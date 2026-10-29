using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using FluentValidation;

namespace Auth.Application.Features.ApiKeys.CreateApiKey;

/// <summary>
/// Validates the CreateApiKeyCommand input fields.
/// </summary>
public class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    /// <summary>The <c>ApiKeys.Environment</c> column's size.</summary>
    private const int EnvironmentMaxLength = 20;

    public CreateApiKeyCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        RuleFor(x => x.Environment)
            .NotEmpty().WithErrorCode(EnvironmentErrors.Required.Code)
            .MaximumLength(EnvironmentMaxLength).WithErrorCode(EnvironmentErrors.TooLong.Code);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithErrorCode(ExpiryErrors.NotInFuture.Code)
            .When(x => x.ExpiresAt.HasValue);

        // OI-128 PR B. The handler asks the same of the domain; these rules carry the field's
        // pointer, so a form can show the refusal under the field it belongs to.
        RuleFor(x => x.PermissionIds)
            .NotEmpty().WithErrorCode(ApiKeyErrors.ScopesRequired.Code);
        RuleFor(x => x.AllowedIps)
            .Must(ips => ips is null || ips.Count <= IpAllowList.MaxEntries)
            .WithErrorCode(ApiKeyErrors.AllowedIpsTooMany.Code);
        RuleForEach(x => x.AllowedIps)
            .Must(IpAllowList.IsValidEntry).WithErrorCode(ApiKeyErrors.AllowedIpInvalid.Code)
            .When(x => x.AllowedIps is not null && x.AllowedIps.Count <= IpAllowList.MaxEntries);
    }
}
