using Auth.Application.Features.Notifications.CreateNotificationLayout;
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
            .NotEmpty().WithMessage("Validation.NotificationTypeName.Required")
            .MaximumLength(200).WithMessage("Validation.NotificationTypeName.MaxLength");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Validation.NotificationTypeDescription.MaxLength");

        RuleFor(x => x.VariablesJson)
            .NotEmpty().WithMessage("Validation.NotificationVariables.InvalidJson")
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonArray)
            .WithMessage("Validation.NotificationVariables.InvalidJson");

        RuleFor(x => x.SampleDataJson)
            .NotEmpty().WithMessage("Validation.NotificationSampleData.InvalidJson")
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithMessage("Validation.NotificationSampleData.InvalidJson");
    }
}
