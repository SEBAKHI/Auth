using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
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
            .IsInEnum().WithErrorCode(NotificationErrors.ChannelInvalid.Code);

        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(NotificationErrors.LayoutNameRequired.Code)
            .MaximumLength(200).WithErrorCode(NotificationErrors.LayoutNameTooLong.Code);

        RuleFor(x => x.DraftContent)
            .NotEmpty().WithErrorCode(NotificationErrors.LayoutContentRequired.Code)
            .MaximumLength(MaxContentLength).WithErrorCode(NotificationErrors.LayoutContentTooLong.Code);

        RuleFor(x => x.DraftStringsJson)
            .Must(BeValidJsonObject).WithErrorCode(NotificationErrors.LayoutStringsInvalidJson.Code);

        RuleFor(x => x.DraftCardColorLight)
            .Must(BeValidOptionalCardColor).WithErrorCode(NotificationErrors.LayoutCardColorInvalid.Code);

        RuleFor(x => x.DraftCardColorDark)
            .Must(BeValidOptionalCardColor).WithErrorCode(NotificationErrors.LayoutCardColorInvalid.Code);
    }

    /// <summary>Empty (the default colour) or #RRGGBB; the rule itself lives on <see cref="CardColor"/>.</summary>
    internal static bool BeValidOptionalCardColor(string? colour) =>
        !CardColor.CreateOptional(colour).IsError;

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
