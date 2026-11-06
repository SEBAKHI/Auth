namespace Auth.Domain.Enums;

/// <summary>
/// What a sign-in's signature counter says about a passkey (S20, PK9; WebAuthn L3 §7.2,
/// "verifying an authentication assertion", the signature-counter step).
/// </summary>
/// <remarks>The values are fixed and never renumbered: tests and log lines name them.</remarks>
public enum PasskeyCounterCheck
{
    /// <summary>
    /// The counter grew, or neither the stored nor the incoming one counts (both zero): the
    /// sign-in may go on, and the new counter and flags are recorded.
    /// </summary>
    Advanced = 0,

    /// <summary>
    /// The counter did not grow while one of the two counts: a sign of a copied or broken
    /// authenticator. The sign-in is refused and the passkey sealed, never removed.
    /// </summary>
    Regressed = 1
}
