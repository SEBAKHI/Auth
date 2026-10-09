namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for a change to an enabled second factor that proves the factor
/// once more first: new recovery codes, or starting an authenticator replacement.
/// </summary>
public record TwoFactorProofRequest
{
    /// <summary>
    /// The 6-digit code from the current authenticator app, or one of the recovery
    /// codes when <see cref="UseRecoveryCode"/> is true.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Code"/> is a recovery code — for a user whose
    /// authenticator app is lost.
    /// </summary>
    public bool UseRecoveryCode { get; init; }
}
