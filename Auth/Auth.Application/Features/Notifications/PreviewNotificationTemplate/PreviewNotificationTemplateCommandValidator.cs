using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.PreviewNotificationTemplate;

/// <summary>
/// Validator for template previews.
/// </summary>
public class PreviewNotificationTemplateCommandValidator
    : AbstractValidator<PreviewNotificationTemplateCommand>
{
    private const int MaxBodyLength = 512_000;

    public PreviewNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.NotificationTypeId)
            .NotEmpty().WithErrorCode(NotificationErrors.TypeIdRequired.Code);

        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithErrorCode(NotificationErrors.LanguageCodeRequired.Code)
            .Must(Languages.IsSupported).WithErrorCode(NotificationErrors.LanguageCodeNotSupported.Code);

        RuleFor(x => x.Subject)
            .MaximumLength(500).WithErrorCode(NotificationErrors.SubjectTooLong.Code);

        RuleFor(x => x.BodyHtml)
            .MaximumLength(MaxBodyLength).WithErrorCode(NotificationErrors.BodyHtmlTooLong.Code);

        RuleFor(x => x.BodyText)
            .MaximumLength(MaxBodyLength).WithErrorCode(NotificationErrors.BodyTextTooLong.Code);
    }
}
