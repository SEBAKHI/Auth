using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.UpdateNotificationType;

/// <summary>
/// Validator for notification type metadata updates.
/// </summary>
public class UpdateNotificationTypeCommandValidator : AbstractValidator<UpdateNotificationTypeCommand>
{
    public UpdateNotificationTypeCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(NotificationErrors.TypeNameRequired.Code)
            .MaximumLength(200).WithErrorCode(NotificationErrors.TypeNameTooLong.Code);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithErrorCode(NotificationErrors.TypeDescriptionTooLong.Code);

        RuleFor(x => x.VariablesJson)
            .NotEmpty().WithErrorCode(NotificationErrors.VariablesInvalidJson.Code)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonArray)
            .WithErrorCode(NotificationErrors.VariablesInvalidJson.Code);

        RuleFor(x => x.SampleDataJson)
            .NotEmpty().WithErrorCode(NotificationErrors.SampleDataInvalidJson.Code)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithErrorCode(NotificationErrors.SampleDataInvalidJson.Code);
    }
}
