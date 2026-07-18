namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to save layout draft edits (live layout untouched until publish).
/// </summary>
public record UpdateNotificationLayoutDraftRequest(
    string Name,
    string DraftContent,
    string DraftStringsJson = "{}",
    DateTime? ExpectedModifiedAt = null);
