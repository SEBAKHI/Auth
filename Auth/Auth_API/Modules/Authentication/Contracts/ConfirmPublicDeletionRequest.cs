namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for step 2 of the public no-login deletion flow: confirm
/// email possession with the verification code.
/// </summary>
public record ConfirmPublicDeletionRequest
{
    /// <summary>
    /// Gets the email address of the account to delete.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the 6-digit verification code.
    /// </summary>
    public string OtpCode { get; init; } = string.Empty;
}
