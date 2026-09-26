using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.CreateNotificationTemplate;

/// <summary>
/// Validator for creating a notification template.
/// </summary>
public class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.NotificationTypeId)
            .NotEmpty().WithErrorCode(NotificationErrors.TypeIdRequired.Code);

        RuleFor(x => x.Channel)
            .IsInEnum().WithErrorCode(NotificationErrors.ChannelInvalid.Code);

        RuleFor(x => x.DefaultLanguage)
            .NotEmpty().WithErrorCode(NotificationErrors.DefaultLanguageRequired.Code)
            .Must(Languages.IsSupported).WithErrorCode(NotificationErrors.DefaultLanguageNotSupported.Code);
    }
}
