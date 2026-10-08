namespace Auth.Domain.Enums;

/// <summary>
/// What a platform administrator's session still has to prove before its token
/// carries platform authority, when <c>TwoFactor:EnforceForPlatformAdmins</c> is on.
/// </summary>
/// <remarks>
/// The values are fixed: a later requirement takes the next free number. The
/// token and the API carry the lowercase claim vocabulary of
/// <see cref="Constants.MfaRequirementValues"/>, never these numbers.
/// </remarks>
public enum MfaRequirement
{
    /// <summary>Nothing: no platform authority, or the session proved two factors.</summary>
    None = 0,

    /// <summary>The account has no second factor enabled: it must set one up.</summary>
    Enroll = 1,

    /// <summary>
    /// The account has a factor and the session proved its first factor only: one
    /// code from the authenticator app, or a recovery code, upgrades it.
    /// </summary>
    StepUp = 2,

    /// <summary>
    /// The session's first factor is unknown — recorded before methods were, or
    /// opened by an emailed code — so it must sign in again, which records it.
    /// </summary>
    Reauthenticate = 3
}
