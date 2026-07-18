using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Notifications.SendTestNotification;

/// <summary>
/// Validator for test sends.
/// </summary>
public class SendTestNotificationCommandValidator : AbstractValidator<SendTestNotificationCommand>
{
    public SendTestNotificationCommandValidator()
    {
        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithMessage("Validation.NotificationLanguage.Required")
            .Must(Languages.IsSupported).WithMessage("Validation.NotificationLanguage.NotSupported");

        RuleFor(x => x.RecipientEmail)
            .NotEmpty().WithMessage("Validation.NotificationRecipientEmail.Required")
            .EmailAddress().WithMessage("Validation.NotificationRecipientEmail.InvalidFormat");
    }
}
