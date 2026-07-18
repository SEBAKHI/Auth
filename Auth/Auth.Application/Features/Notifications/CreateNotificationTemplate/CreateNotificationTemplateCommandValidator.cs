using Auth.Domain.Constants;
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
            .NotEmpty().WithMessage("Validation.NotificationTypeId.Required");

        RuleFor(x => x.Channel)
            .IsInEnum().WithMessage("Validation.NotificationChannel.Invalid");

        RuleFor(x => x.DefaultLanguage)
            .NotEmpty().WithMessage("Validation.NotificationLanguage.Required")
            .Must(Languages.IsSupported).WithMessage("Validation.NotificationLanguage.NotSupported");
    }
}
