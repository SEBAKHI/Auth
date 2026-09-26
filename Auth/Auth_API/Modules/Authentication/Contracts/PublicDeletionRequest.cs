namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for step 1 of the public no-login deletion flow.
/// </summary>
public record PublicDeletionRequest
{
    /// <summary>
    /// Gets the email address of the account to delete.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
