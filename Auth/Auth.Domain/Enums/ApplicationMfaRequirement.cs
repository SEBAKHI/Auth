namespace Auth.Domain.Enums;

/// <summary>
/// What an application that requires two-step verification asks of a sign-in session
/// before it may issue that application a code (S23, AM2). Decided by
/// <c>IApplicationMfaPolicy</c> from the application's flag, what the session proved
/// and whether the account holds a qualifying second factor.
/// </summary>
/// <remarks>
/// The values are explicit and never renumbered: they are signed into the
/// <c>__Host-auth_appmfa</c> ticket as numbers, so a renumbering would make a ticket
/// issued before a deploy name another outcome after it.
/// </remarks>
public enum ApplicationMfaRequirement
{
    /// <summary>Nothing more: the application does not ask, or the session proved two factors.</summary>
    None = 0,

    /// <summary>
    /// The session proved a first factor and the account has an authenticator app: sign in
    /// again and answer its code.
    /// </summary>
    StepUp = 1,

    /// <summary>
    /// The account's only qualifying factor is a passkey (S20): a passkey sign-in, which
    /// proves two factors on its own, is the only session that satisfies the application.
    /// Produced only while passkeys count (an RP ID is configured and the schema is
    /// deployed), so never on an account that has no live passkey.
    /// </summary>
    Passkey = 2,

    /// <summary>
    /// The account has no qualifying second factor (an emailed code never qualifies): set one
    /// up first.
    /// </summary>
    Enroll = 3,

    /// <summary>
    /// The session proved no first factor this application can rely on (nothing recorded, or
    /// an emailed code of an account that has a password): sign in again.
    /// </summary>
    Reauthenticate = 4
}
