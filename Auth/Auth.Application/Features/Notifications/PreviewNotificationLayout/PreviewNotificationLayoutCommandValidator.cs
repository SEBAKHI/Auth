using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.PreviewNotificationLayout;

/// <summary>
/// Validator for layout previews.
/// </summary>
public class PreviewNotificationLayoutCommandValidator : AbstractValidator<PreviewNotificationLayoutCommand>
{
    private const int MaxContentLength = 512_000;

    public PreviewNotificationLayoutCommandValidator()
    {
        RuleFor(x => x.LayoutContent)
            .NotEmpty().WithErrorCode(NotificationErrors.PreviewLayoutContentRequired.Code)
            .MaximumLength(MaxContentLength).WithErrorCode(NotificationErrors.PreviewLayoutContentTooLong.Code);

        RuleFor(x => x.LayoutStringsJson)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithErrorCode(NotificationErrors.PreviewLayoutStringsInvalidJson.Code);

        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithErrorCode(NotificationErrors.LanguageCodeRequired.Code)
            .Must(Languages.IsSupported).WithErrorCode(NotificationErrors.LanguageCodeNotSupported.Code);
    }
}
