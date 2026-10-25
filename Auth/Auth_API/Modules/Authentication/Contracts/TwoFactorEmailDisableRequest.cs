namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for switching the emailed second factor off.
/// </summary>
public record TwoFactorEmailDisableRequest
{
    /// <summary>
    /// The 6-digit code emailed for switching the emailed code off, or one of the
    /// recovery codes when <see cref="UseRecoveryCode"/> is true. Absent or empty asks
    /// for that email instead (answered 202).
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Whether <see cref="Code"/> is a recovery code, for an owner whose mailbox is out
    /// of reach.
    /// </summary>
    public bool UseRecoveryCode { get; init; }
}
