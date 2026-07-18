namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Read model for the admin template list: template row joined with its type
/// and application display fields plus version summary.
/// </summary>
public sealed record NotificationTemplateListItem(
    Guid Id,
    Guid NotificationTypeId,
    string TypeCode,
    string TypeName,
    bool TypeIsSystem,
    Guid? ApplicationId,
    string? ApplicationName,
    byte Channel,
    string DefaultLanguage,
    Guid? PublishedVersionId,
    int? PublishedVersionNumber,
    Guid? DraftVersionId,
    int? DraftVersionNumber,
    int TranslationCount,
    DateTime CreatedAt,
    DateTime? ModifiedAt);
