namespace Auth.Domain.Enums;

/// <summary>
/// Represents the status of a user account.
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// User account is active and can authenticate.
    /// </summary>
    Active = 1,

    /// <summary>
    /// User account is inactive and cannot authenticate.
    /// </summary>
    Inactive = 2,

    /// <summary>
    /// User account is locked due to failed login attempts.
    /// </summary>
    Locked = 3,

    /// <summary>
    /// User account is pending activation (e.g., email verification).
    /// </summary>
    Pending = 4
}
