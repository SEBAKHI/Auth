namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to save draft edits: translation upserts, removals, and change note.
/// ExpectedModifiedAt enables optimistic concurrency (409 on conflict).
/// </summary>
public record UpdateNotificationTemplateDraftRequest(
    List<DraftTranslationRequest> Translations,
    List<string>? RemoveLanguages = null,
    string? ChangeNote = null,
    DateTime? ExpectedModifiedAt = null);

/// <summary>
/// One translation upsert within a draft save.
/// </summary>
public record DraftTranslationRequest(
    string LanguageCode,
    string Subject,
    string BodyHtml,
    string? BodyText = null);
