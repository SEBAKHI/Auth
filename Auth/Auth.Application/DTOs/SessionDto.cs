using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

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
    public DeviceType DeviceType { get; set; }

    /// <summary>
    /// The browser this session was started from, or null when it cannot be
    /// attributed to one — a client that sent no identifier, or a browser the
    /// user has since forgotten. The UI groups on this and shows the nulls
    /// together rather than inventing a parent for them.
    /// </summary>
    public Guid? KnownDeviceId { get; set; }

    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }
}
