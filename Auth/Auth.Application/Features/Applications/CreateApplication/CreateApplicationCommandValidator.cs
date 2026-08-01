using Auth.Application.Validators.Rules;
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
        RuleFor(x => x.BaseUrl).IsValidUrl().When(x => x.BaseUrl is not null);
        RuleFor(x => x.LogoUrl).IsValidUrl().When(x => x.LogoUrl is not null);
        RuleFor(x => x.ContactEmail!).IsValidEmail().When(x => x.ContactEmail is not null);
        RuleFor(x => x.SessionTimeoutMinutes).GreaterThan(0);
        RuleFor(x => x.MaxConcurrentSessions).GreaterThan(0);
        RuleFor(x => x.ReauthenticationMaxAgeMinutes).IsValidReauthenticationMaxAge();

        // Same allowlist rules as the update path: a redirect URI registered at
        // creation is exactly as much of a security boundary as one added later.
        RuleFor(x => x.RedirectUris!)
            .IsWithinRedirectUriLimit()
            .When(x => x.RedirectUris is not null);

        RuleForEach(x => x.RedirectUris!)
            .IsValidRedirectUri()
            .When(x => x.RedirectUris is not null);
    }
}
