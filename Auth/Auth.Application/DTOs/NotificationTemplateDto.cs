namespace Auth.Application.DTOs;

/// <summary>
/// List-row DTO for notification templates (admin list page).
/// </summary>
public class NotificationTemplateDto
{
    public Guid Id { get; set; }
    public Guid NotificationTypeId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public bool TypeIsSystem { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public int? PublishedVersionNumber { get; set; }
    public bool HasDraft { get; set; }
    public int? DraftVersionNumber { get; set; }
    public int TranslationCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Paged wrapper for the notification template list.
/// </summary>
public class PagedNotificationTemplatesDto
{
    public List<NotificationTemplateDto> Templates { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
