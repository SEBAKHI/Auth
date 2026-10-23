namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for emailing the code that completes a pending sign-in.
/// </summary>
public record TwoFactorSignInEmailCodeRequest
{
    /// <summary>
    /// The opaque challenge token returned by the login endpoint.
    /// </summary>
    public string ChallengeToken { get; init; } = string.Empty;
}
