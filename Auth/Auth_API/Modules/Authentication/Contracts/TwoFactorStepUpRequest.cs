namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for proving the second factor inside the current session.
/// </summary>
public record TwoFactorStepUpRequest
{
    /// <summary>
    /// The 6-digit code from the authenticator app, or one of the recovery codes
    /// when <see cref="UseRecoveryCode"/> is true.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Code"/> is a recovery code — for a user whose
    /// authenticator app is lost.
    /// </summary>
    public bool UseRecoveryCode { get; init; }
}
