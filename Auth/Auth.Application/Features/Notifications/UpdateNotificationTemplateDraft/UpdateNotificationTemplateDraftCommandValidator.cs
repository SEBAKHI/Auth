using Auth.Domain.Constants;
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
            .MaximumLength(500).WithMessage("Validation.NotificationChangeNote.MaxLength");

        RuleForEach(x => x.Translations).ChildRules(translation =>
        {
            translation.RuleFor(t => t.LanguageCode)
                .NotEmpty().WithMessage("Validation.NotificationLanguage.Required")
                .Must(Languages.IsSupported).WithMessage("Validation.NotificationLanguage.NotSupported");

            translation.RuleFor(t => t.Subject)
                .NotEmpty().WithMessage("Validation.NotificationSubject.Required")
                .MaximumLength(500).WithMessage("Validation.NotificationSubject.MaxLength");

            translation.RuleFor(t => t.BodyHtml)
                .NotEmpty().WithMessage("Validation.NotificationBody.Required")
                .MaximumLength(MaxBodyLength).WithMessage("Validation.NotificationBody.MaxLength");

            translation.RuleFor(t => t.BodyText)
                .MaximumLength(MaxBodyLength).WithMessage("Validation.NotificationBody.MaxLength");
        });

        RuleForEach(x => x.RemoveLanguages)
            .Must(language => Languages.IsSupported(language))
            .WithMessage("Validation.NotificationLanguage.NotSupported");
    }
}
