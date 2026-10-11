namespace Auth.Domain.Enums;

/// <summary>
/// The authenticator whose consecutive failures reached the hard limit.
/// </summary>
/// <remarks>
/// The values are fixed: a later factor takes the next free number and no member
/// is ever renumbered.
/// </remarks>
public enum HardLockedFactor
{
    /// <summary>The account's password, counted in <c>Users.FailedLoginAttempts</c>.</summary>
    Password = 1,

    /// <summary>The second factor, counted in <c>TwoFactorAuth.FailedAttempts</c>.</summary>
    SecondFactor = 2
}
