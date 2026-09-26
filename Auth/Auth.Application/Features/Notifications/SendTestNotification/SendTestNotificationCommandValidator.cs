using Auth.Domain.Constants;
using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(NotificationErrors.LanguageCodeRequired.Code)
            .Must(Languages.IsSupported).WithErrorCode(NotificationErrors.LanguageCodeNotSupported.Code);

        RuleFor(x => x.RecipientEmail)
            .NotEmpty().WithErrorCode(NotificationErrors.RecipientEmailRequired.Code)
            .EmailAddress().WithErrorCode(NotificationErrors.RecipientEmailInvalidFormat.Code);
    }
}
