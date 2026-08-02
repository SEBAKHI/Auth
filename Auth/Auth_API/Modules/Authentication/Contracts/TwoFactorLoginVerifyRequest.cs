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

    /// <summary>
    /// Optional stable client identifier, used only to tell one device from
    /// another when deciding whether a sign-in is worth alerting the owner
    /// about. Never an authorization input — it is client-supplied.
    /// </summary>
    public string? DeviceId { get; init; }
}
