using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(NotificationErrors.LayoutNameRequired.Code)
            .MaximumLength(200).WithErrorCode(NotificationErrors.LayoutNameTooLong.Code);

        RuleFor(x => x.DraftContent)
            .NotEmpty().WithErrorCode(NotificationErrors.LayoutContentRequired.Code)
            .MaximumLength(MaxContentLength).WithErrorCode(NotificationErrors.LayoutContentTooLong.Code);

        RuleFor(x => x.DraftStringsJson)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithErrorCode(NotificationErrors.LayoutStringsInvalidJson.Code);
    }
}
