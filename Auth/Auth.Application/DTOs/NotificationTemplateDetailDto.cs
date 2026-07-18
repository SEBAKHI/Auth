namespace Auth.Application.DTOs;

/// <summary>
/// Full editor DTO for one notification template: type metadata (variable
/// catalog + sample data), version history, and the draft/published translations.
/// </summary>
public class NotificationTemplateDetailDto
{
    public Guid Id { get; set; }
    public Guid NotificationTypeId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public bool TypeIsSystem { get; set; }
    public string TypeVariablesJson { get; set; } = "[]";
    public string TypeSampleDataJson { get; set; } = "{}";
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = string.Empty;
    public Guid? PublishedVersionId { get; set; }
    public Guid? DraftVersionId { get; set; }
    public NotificationTemplateVersionDto? PublishedVersion { get; set; }
    public NotificationTemplateVersionDto? DraftVersion { get; set; }
    public List<NotificationTemplateVersionSummaryDto> Versions { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// One template version with all its translations.
/// </summary>
public class NotificationTemplateVersionDto
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public List<NotificationTranslationDto> Translations { get; set; } = [];
}

/// <summary>
/// Version-history row (no translation bodies).
/// </summary>
public class NotificationTemplateVersionSummaryDto
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public bool IsPublished { get; set; }
    public bool IsDraft { get; set; }
    public int TranslationCount { get; set; }
}

/// <summary>
/// One language's content within a version.
/// </summary>
public class NotificationTranslationDto
{
    public string LanguageCode { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string? BodyText { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
