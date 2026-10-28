namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to save layout draft edits (live layout untouched until publish).
/// </summary>
/// <param name="DraftCardColorLight">The light-mode card colour as #RRGGBB, an empty string for the default, or absent to keep the saved colour.</param>
/// <param name="DraftCardColorDark">The dark-mode card colour as #RRGGBB, an empty string for the default, or absent to keep the saved colour.</param>
public record UpdateNotificationLayoutDraftRequest(
    string Name,
    string DraftContent,
    string DraftStringsJson = "{}",
    DateTime? ExpectedModifiedAt = null,
    string? DraftCardColorLight = null,
    string? DraftCardColorDark = null);
