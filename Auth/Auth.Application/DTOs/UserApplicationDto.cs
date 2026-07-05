namespace Auth.Application.DTOs;

/// <summary>
/// One application a user can access, with how the access is obtained.
/// </summary>
public class UserApplicationDto
{
    public Guid ApplicationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// How the user obtains access: "direct", "organization", or "both".
    /// </summary>
    public string AccessSource { get; set; } = string.Empty;
}
