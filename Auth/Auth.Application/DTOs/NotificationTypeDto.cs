namespace Auth.Application.DTOs;

/// <summary>
/// DTO for a notification type with its variable catalog and preview sample data.
/// </summary>
public class NotificationTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public string VariablesJson { get; set; } = "[]";
    public string SampleDataJson { get; set; } = "{}";
    public bool IsActive { get; set; }
}
