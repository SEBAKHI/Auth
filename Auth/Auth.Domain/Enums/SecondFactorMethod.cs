namespace Auth.Domain.Enums;

/// <summary>
/// The second factor a sign-in code was presented as. Each member is served by
/// exactly one proof strategy, chosen by this value rather than by branching.
/// </summary>
/// <remarks>
/// The values are fixed: a later factor takes the next free number and no member
/// is ever renumbered.
/// </remarks>
public enum SecondFactorMethod
{
    /// <summary>A six-digit code from the user's authenticator app.</summary>
    Totp = 1,

    /// <summary>One of the user's single-use recovery codes.</summary>
    RecoveryCode = 2
}
