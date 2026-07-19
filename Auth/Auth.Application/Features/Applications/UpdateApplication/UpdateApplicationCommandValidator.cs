using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Applications.UpdateApplication;

/// <summary>
/// Validates the UpdateApplicationCommand input fields.
/// </summary>
public class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
{
    public UpdateApplicationCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        RuleFor(x => x.BaseUrl).IsValidUrl().When(x => x.BaseUrl is not null);
        RuleFor(x => x.LogoUrl).IsValidUrl().When(x => x.LogoUrl is not null);
        RuleFor(x => x.ContactEmail!).IsValidEmail().When(x => x.ContactEmail is not null);
        RuleFor(x => x.SessionTimeoutMinutes).GreaterThan(0);
        RuleFor(x => x.MaxConcurrentSessions).GreaterThan(0);

        RuleFor(x => x.RedirectUris!)
            .Must(uris => uris.Count <= 20)
            .WithMessage("Validation.RedirectUri.TooMany")
            .When(x => x.RedirectUris is not null);

        RuleForEach(x => x.RedirectUris!)
            .Must(BeAValidRedirectUri)
            .WithMessage("Validation.RedirectUri.Invalid")
            .When(x => x.RedirectUris is not null);
    }

    /// <summary>
    /// A registered redirect URI must be absolute, fragment-free, at most 500
    /// characters (DB column), and use https — plain http is allowed only for
    /// localhost during development (OAuth 2.0 Security BCP).
    /// </summary>
    private static bool BeAValidRedirectUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 500)
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Fragment.Length > 0)
        {
            return false;
        }

        if (parsed.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
    }
}
