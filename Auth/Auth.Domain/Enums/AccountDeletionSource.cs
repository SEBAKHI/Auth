namespace Auth.Domain.Enums;

/// <summary>
/// Represents where an account deletion request originated.
/// Values match the CK_AccountDeletionRequests_Source check constraint.
/// </summary>
public enum AccountDeletionSource
{
    /// <summary>
    /// Requested from inside the application by the authenticated user.
    /// </summary>
    InApp = 1,

    /// <summary>
    /// Requested through the public no-login deletion page, verified by email
    /// possession (OTP).
    /// </summary>
    PublicWeb = 2
}
