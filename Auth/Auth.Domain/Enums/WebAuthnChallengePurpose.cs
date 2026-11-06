namespace Auth.Domain.Enums;

/// <summary>
/// What a WebAuthn challenge was issued for (S20, PK6). Stored as a TINYINT in
/// <c>WebAuthnChallenges.Purpose</c>; the values are explicit and never renumbered, and the
/// conditional statement that consumes a challenge names the purpose it expects, so a
/// challenge issued for one ceremony can never answer the other.
/// </summary>
public enum WebAuthnChallengePurpose : byte
{
    /// <summary>A signed-in account adding a passkey: the row names the account.</summary>
    Registration = 1,

    /// <summary>A sign-in with a passkey (S20 PR B): the row names no account.</summary>
    Assertion = 2
}
