using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Notifications.PreviewNotificationLayout;

/// <summary>
/// Validator for layout previews.
/// </summary>
public class PreviewNotificationLayoutCommandValidator : AbstractValidator<PreviewNotificationLayoutCommand>
{
    private const int MaxContentLength = 512_000;

    public PreviewNotificationLayoutCommandValidator()
    {
        RuleFor(x => x.LayoutContent)
            .NotEmpty().WithMessage("Validation.NotificationLayoutContent.Required")
            .MaximumLength(MaxContentLength).WithMessage("Validation.NotificationLayoutContent.MaxLength");

        RuleFor(x => x.LayoutStringsJson)
            .Must(CreateNotificationLayoutCommandValidator.BeValidJsonObject)
            .WithMessage("Validation.NotificationLayoutStrings.InvalidJson");

        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithMessage("Validation.NotificationLanguage.Required")
            .Must(Languages.IsSupported).WithMessage("Validation.NotificationLanguage.NotSupported");
    }
}
