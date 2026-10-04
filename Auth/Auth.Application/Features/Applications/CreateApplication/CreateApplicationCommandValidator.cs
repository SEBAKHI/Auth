using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Applications.CreateApplication;

/// <summary>
/// Validates the CreateApplicationCommand input fields.
/// </summary>
public class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(x => x.Code).IsValidCode();
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        // Both URL columns are NVARCHAR(500); the old 2048 ceiling let longer values through to a failed INSERT.
        RuleFor(x => x.BaseUrl)
            .MaximumLength(500).WithErrorCode(ApplicationErrors.BaseUrlTooLong.Code)
            .When(x => x.BaseUrl is not null);
        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithErrorCode(ApplicationErrors.LogoUrlTooLong.Code)
            .When(x => x.LogoUrl is not null);
        RuleFor(x => x.ContactEmail!).IsValidContactEmail().When(x => x.ContactEmail is not null);
        RuleFor(x => x.SessionTimeoutMinutes).GreaterThan(0).WithErrorCode(ApplicationErrors.SessionTimeoutNotPositive.Code);
        RuleFor(x => x.MaxConcurrentSessions).GreaterThan(0).WithErrorCode(ApplicationErrors.MaxConcurrentSessionsNotPositive.Code);
        RuleFor(x => x.ReauthenticationMaxAgeMinutes).IsValidReauthenticationMaxAge();
        RuleFor(x => x.AccessMode).IsInEnum().WithErrorCode(ApplicationErrors.AccessModeInvalid.Code);

        // Same allowlist rules as the update path: a redirect URI registered at
        // creation is exactly as much of a security boundary as one added later.
        RuleFor(x => x.RedirectUris!)
            .IsWithinRedirectUriLimit()
            .When(x => x.RedirectUris is not null);

        RuleForEach(x => x.RedirectUris!)
            .IsValidRedirectUri()
            .When(x => x.RedirectUris is not null);

        RuleFor(x => x.AllowedScopes!)
            .IsValidAllowedScopes()
            .When(x => x.AllowedScopes is not null);
    }
}
