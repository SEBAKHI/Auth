using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Platform.UpdatePlatformSettings;

/// <summary>
/// Validates the UpdatePlatformSettingsCommand input fields.
/// </summary>
public class UpdatePlatformSettingsCommandValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsCommandValidator()
    {
        RuleFor(x => x.PlatformName).IsValidName();
        RuleFor(x => x.LogoUrl).IsValidUrl().When(x => x.LogoUrl is not null);
        RuleFor(x => x.LogoUrlDark).IsValidUrl().When(x => x.LogoUrlDark is not null);
        RuleFor(x => x.FaviconUrl).IsValidUrl().When(x => x.FaviconUrl is not null);
    }
}
