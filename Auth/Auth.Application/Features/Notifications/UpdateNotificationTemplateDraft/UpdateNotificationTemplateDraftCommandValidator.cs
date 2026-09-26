using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;

/// <summary>
/// Validator for draft saves: language allow-list plus size caps (oversized
/// bodies are a defense against render-time resource abuse).
/// </summary>
public class UpdateNotificationTemplateDraftCommandValidator
    : AbstractValidator<UpdateNotificationTemplateDraftCommand>
{
    private const int MaxBodyLength = 512_000;

    public UpdateNotificationTemplateDraftCommandValidator()
    {
        RuleFor(x => x.ChangeNote)
            .MaximumLength(500).WithErrorCode(NotificationErrors.ChangeNoteTooLong.Code);

        RuleForEach(x => x.Translations).ChildRules(translation =>
        {
            translation.RuleFor(t => t.LanguageCode)
                .NotEmpty().WithErrorCode(NotificationErrors.TranslationLanguageRequired.Code)
                .Must(Languages.IsSupported).WithErrorCode(NotificationErrors.TranslationLanguageNotSupported.Code);

            translation.RuleFor(t => t.Subject)
                .NotEmpty().WithErrorCode(NotificationErrors.SubjectRequired.Code)
                .MaximumLength(500).WithErrorCode(NotificationErrors.SubjectTooLong.Code);

            translation.RuleFor(t => t.BodyHtml)
                .NotEmpty().WithErrorCode(NotificationErrors.BodyHtmlRequired.Code)
                .MaximumLength(MaxBodyLength).WithErrorCode(NotificationErrors.BodyHtmlTooLong.Code);

            translation.RuleFor(t => t.BodyText)
                .MaximumLength(MaxBodyLength).WithErrorCode(NotificationErrors.BodyTextTooLong.Code);
        });

        RuleForEach(x => x.RemoveLanguages)
            .Must(language => Languages.IsSupported(language))
            .WithErrorCode(NotificationErrors.RemovedLanguageNotSupported.Code);
    }
}
