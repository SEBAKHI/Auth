using Auth.Domain.Constants;
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
            .NotEmpty().WithMessage("Validation.NotificationTypeId.Required");

        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithMessage("Validation.NotificationLanguage.Required")
            .Must(Languages.IsSupported).WithMessage("Validation.NotificationLanguage.NotSupported");

        RuleFor(x => x.Subject)
            .MaximumLength(500).WithMessage("Validation.NotificationSubject.MaxLength");

        RuleFor(x => x.BodyHtml)
            .MaximumLength(MaxBodyLength).WithMessage("Validation.NotificationBody.MaxLength");

        RuleFor(x => x.BodyText)
            .MaximumLength(MaxBodyLength).WithMessage("Validation.NotificationBody.MaxLength");
    }
}
