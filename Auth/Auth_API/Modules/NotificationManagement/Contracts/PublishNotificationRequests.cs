namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Identifies the exact template draft an administrator reviewed before publishing.
/// </summary>
public record PublishNotificationTemplateRequest(
    Guid ExpectedDraftVersionId,
    DateTime ExpectedRevisionAt);

/// <summary>
/// Identifies the exact live template version an administrator reviewed before unpublishing.
/// </summary>
public record UnpublishNotificationTemplateRequest(Guid ExpectedPublishedVersionId);

/// <summary>
/// Identifies the exact saved layout draft an administrator reviewed before publishing.
/// </summary>
public record PublishNotificationLayoutRequest(DateTime ExpectedRevisionAt);
