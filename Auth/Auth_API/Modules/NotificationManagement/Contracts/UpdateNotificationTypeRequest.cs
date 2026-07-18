namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to update a notification type's admin-editable metadata.
/// </summary>
public record UpdateNotificationTypeRequest(
    string Name,
    string? Description,
    string VariablesJson,
    string SampleDataJson);
