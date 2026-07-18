using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// One queued (pre-rendered) notification in the delivery log. Content is final
/// at enqueue time; the dispatcher only moves the delivery lifecycle. The row
/// doubles as an auditable record of what was sent, from which application,
/// by which template version, to whom, in which language, and who triggered it.
/// </summary>
public class NotificationOutboxMessage : EntityBase
{
    public string NotificationTypeCode { get; private set; } = string.Empty;
    public NotificationChannelType Channel { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public string? RecipientName { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public string LanguageCode { get; private set; } = string.Empty;
    public Guid? TemplateId { get; private set; }
    public Guid? TemplateVersionId { get; private set; }
    public int? TemplateVersionNumber { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string BodyHtml { get; private set; } = string.Empty;
    public string? BodyText { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? ClaimedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private NotificationOutboxMessage() : base()
    {
    }

    public NotificationOutboxMessage(
        Guid id,
        string notificationTypeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        string recipient,
        string? recipientName,
        Guid? recipientUserId,
        string languageCode,
        Guid? templateId,
        Guid? templateVersionId,
        int? templateVersionNumber,
        string subject,
        string bodyHtml,
        string? bodyText,
        NotificationDeliveryStatus status,
        int attemptCount,
        DateTime nextAttemptAt,
        DateTime? claimedAt,
        DateTime? sentAt,
        string? lastError,
        DateTime createdAt,
        Guid? createdBy) : base(id)
    {
        NotificationTypeCode = notificationTypeCode;
        Channel = channel;
        ApplicationId = applicationId;
        Recipient = recipient;
        RecipientName = recipientName;
        RecipientUserId = recipientUserId;
        LanguageCode = languageCode;
        TemplateId = templateId;
        TemplateVersionId = templateVersionId;
        TemplateVersionNumber = templateVersionNumber;
        Subject = subject;
        BodyHtml = bodyHtml;
        BodyText = bodyText;
        Status = status;
        AttemptCount = attemptCount;
        NextAttemptAt = nextAttemptAt;
        ClaimedAt = claimedAt;
        SentAt = sentAt;
        LastError = lastError;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Creates a pending outbox message ready for immediate dispatch.
    /// </summary>
    public static NotificationOutboxMessage Create(
        string notificationTypeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        string recipient,
        string? recipientName,
        Guid? recipientUserId,
        string languageCode,
        Guid? templateId,
        Guid? templateVersionId,
        int? templateVersionNumber,
        string subject,
        string bodyHtml,
        string? bodyText,
        Guid? createdBy)
    {
        return new NotificationOutboxMessage
        {
            NotificationTypeCode = notificationTypeCode,
            Channel = channel,
            ApplicationId = applicationId,
            Recipient = recipient,
            RecipientName = recipientName,
            RecipientUserId = recipientUserId,
            LanguageCode = languageCode,
            TemplateId = templateId,
            TemplateVersionId = templateVersionId,
            TemplateVersionNumber = templateVersionNumber,
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            Status = NotificationDeliveryStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }
}
