using Auth.Application.Features.Notifications.CreateNotificationLayout;
using FluentValidation;

namespace Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;

/// <summary>
/// Validator for layout draft saves (reuses the create-layout JSON rule).
/// </summary>
public class UpdateNotificationLayoutDraftCommandValidator
    : AbstractValidator<UpdateNotificationLayoutDraftCommand>
{
    private const int MaxContentLength = 512_000;

    public UpdateNotificationLayoutDraftCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Validation.NotificationLayoutName.Required")
            .MaximumLength(200).WithMessage("Validation.NotificationLayoutName.MaxLength");

        RuleFor(x => x.DraftContent)
            .NotEmpty().WithMessage("Validation.NotificationLayoutContent.Required")
            .MaximumLength(MaxContentLength).WithMessage("Validation.NotificationLayoutContent.MaxLength");

        RuleFor(x => x.DraftStringsJson)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithMessage("Validation.NotificationLayoutStrings.InvalidJson");
    }
}
