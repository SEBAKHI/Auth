namespace Auth_Lib.DTOs;

/// <summary>
/// Data transfer object for user session information.
/// </summary>
public class SessionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceName { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }
}
