namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to roll the published pointer back to a previous version.
/// </summary>
public record RollbackNotificationTemplateRequest(Guid TargetVersionId);
