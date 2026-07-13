namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for completing a two-factor login challenge.
/// </summary>
public record TwoFactorLoginVerifyRequest
{
    /// <summary>
    /// The opaque challenge token returned by the login endpoint.
    /// </summary>
    public required string ChallengeToken { get; init; }

    /// <summary>
    /// The 6-digit TOTP code from the authenticator app, or a recovery code
    /// when <see cref="UseRecoveryCode"/> is true.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Whether <see cref="Code"/> is a recovery code instead of a TOTP code.
    /// </summary>
    public bool UseRecoveryCode { get; init; }
}
