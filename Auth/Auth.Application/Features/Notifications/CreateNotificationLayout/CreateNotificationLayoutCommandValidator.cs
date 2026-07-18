using System.Text.Json;
using FluentValidation;

namespace Auth.Application.Features.Notifications.CreateNotificationLayout;

/// <summary>
/// Validator for creating a layout.
/// </summary>
public class CreateNotificationLayoutCommandValidator : AbstractValidator<CreateNotificationLayoutCommand>
{
    private const int MaxContentLength = 512_000;

    public CreateNotificationLayoutCommandValidator()
    {
        RuleFor(x => x.Channel)
            .IsInEnum().WithMessage("Validation.NotificationChannel.Invalid");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Validation.NotificationLayoutName.Required")
            .MaximumLength(200).WithMessage("Validation.NotificationLayoutName.MaxLength");

        RuleFor(x => x.DraftContent)
            .NotEmpty().WithMessage("Validation.NotificationLayoutContent.Required")
            .MaximumLength(MaxContentLength).WithMessage("Validation.NotificationLayoutContent.MaxLength");

        RuleFor(x => x.DraftStringsJson)
            .Must(BeValidJsonObject).WithMessage("Validation.NotificationLayoutStrings.InvalidJson");
    }

    internal static bool BeValidJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool BeValidJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
