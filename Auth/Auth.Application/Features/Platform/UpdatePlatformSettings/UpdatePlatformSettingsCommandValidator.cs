using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Platform.UpdatePlatformSettings;

/// <summary>
/// Validates the UpdatePlatformSettingsCommand input fields.
/// </summary>
public class UpdatePlatformSettingsCommandValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsCommandValidator()
    {
        RuleFor(x => x.PlatformName)
            .NotEmpty().WithErrorCode(SystemSettingsErrors.PlatformNameRequired.Code)
            .MaximumLength(200).WithErrorCode(SystemSettingsErrors.PlatformNameTooLong.Code);
        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithErrorCode(SystemSettingsErrors.LogoUrlTooLong.Code)
            .When(x => x.LogoUrl is not null);
        RuleFor(x => x.LogoUrlDark)
            .MaximumLength(500).WithErrorCode(SystemSettingsErrors.LogoUrlDarkTooLong.Code)
            .When(x => x.LogoUrlDark is not null);
        RuleFor(x => x.FaviconUrl)
            .MaximumLength(500).WithErrorCode(SystemSettingsErrors.FaviconUrlTooLong.Code)
            .When(x => x.FaviconUrl is not null);
    }
}
