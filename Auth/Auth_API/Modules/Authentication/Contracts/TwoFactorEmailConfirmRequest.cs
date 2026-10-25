namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for making the emailed code the account's second factor.
/// </summary>
public record TwoFactorEmailConfirmRequest
{
    /// <summary>
    /// The 6-digit code emailed by <c>POST /api/v1/auth/2fa/email/enable</c>.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
