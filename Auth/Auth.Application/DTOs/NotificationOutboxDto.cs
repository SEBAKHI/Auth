namespace Auth.Application.DTOs;

/// <summary>
/// List-row DTO for the notification delivery log (no rendered bodies —
/// those load through the detail endpoint only).
/// </summary>
public class NotificationOutboxMessageDto
{
    public Guid Id { get; set; }
    public string NotificationTypeCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public Guid? RecipientUserId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public Guid? TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public int? TemplateVersionNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>
/// Detail DTO: one delivery-log entry including the exact rendered content
/// that was (or will be) sent.
/// </summary>
public class NotificationOutboxMessageDetailDto : NotificationOutboxMessageDto
{
    public string BodyHtml { get; set; } = string.Empty;
    public string? BodyText { get; set; }
    public DateTime? ClaimedAt { get; set; }
}

/// <summary>
/// Paged wrapper for the delivery log.
/// </summary>
public class PagedNotificationOutboxDto
{
    public List<NotificationOutboxMessageDto> Messages { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
