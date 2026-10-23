namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for completing a two-factor login challenge.
/// </summary>
public record TwoFactorLoginVerifyRequest
{
    /// <summary>
    /// The opaque challenge token returned by the login endpoint.
    /// </summary>
    public string ChallengeToken { get; init; } = string.Empty;

    /// <summary>
    /// The 6-digit TOTP code from the authenticator app, or a recovery code
    /// when <see cref="UseRecoveryCode"/> is true.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Code"/> is a recovery code instead of a TOTP code. Read only
    /// when <see cref="Method"/> is absent.
    /// </summary>
    public bool UseRecoveryCode { get; init; }

    /// <summary>
    /// The factor <see cref="Code"/> is presented as: <c>totp</c>, <c>recovery</c> or
    /// <c>email</c> — one of the login response's <c>twoFactorMethods</c>. Optional:
    /// absent, <see cref="UseRecoveryCode"/> decides, as it always has.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Optional stable client identifier, used only to tell one device from
    /// another when deciding whether a sign-in is worth alerting the owner
    /// about. Never an authorization input — it is client-supplied.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Gets whether the person ticked "Stay signed in" on the sign-in page that
    /// opened this challenge (OI-154). Optional; absent or false is a browser
    /// session.
    /// </summary>
    public bool StaySignedIn { get; init; }
}
