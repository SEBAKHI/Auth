namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Read model for the admin delivery-log list: one outbox row without the MAX
/// body columns (those load only through the detail view).
/// </summary>
public sealed record NotificationOutboxListItem(
    Guid Id,
    string NotificationTypeCode,
    byte Channel,
    Guid? ApplicationId,
    string? ApplicationName,
    string Recipient,
    string? RecipientName,
    Guid? RecipientUserId,
    string LanguageCode,
    Guid? TemplateId,
    Guid? TemplateVersionId,
    int? TemplateVersionNumber,
    string Subject,
    byte Status,
    int AttemptCount,
    DateTime NextAttemptAt,
    DateTime? SentAt,
    string? LastError,
    DateTime CreatedAt,
    Guid? CreatedBy);
