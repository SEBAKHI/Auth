namespace Auth.Domain.ReadModels.Secrets;

/// <summary>
/// Live counts of the credentials a key rotation would break, gathered in one
/// round trip so the administrator sees a single consistent picture rather than
/// six numbers read at six different instants.
/// </summary>
/// <remarks>
/// Every count is over distinct non-deleted users except where the field name
/// says otherwise: an administrator reads "affected" as "people", and one person
/// with a laptop, a phone and a tablet is one person, not three sessions.
/// </remarks>
public sealed record SecretRotationImpactSnapshot
{
    /// <summary>
    /// Users whose access token was minted recently enough to still be inside
    /// its lifetime. These are the accounts that will see a 401 within seconds
    /// of an RSA rotation taking effect.
    /// </summary>
    public required int UsersWithLiveAccessTokens { get; init; }

    /// <summary>
    /// Users holding a session that has neither ended nor expired — the full
    /// blast radius of an RSA rotation, since each of them hits at least one
    /// 401 before their session is over.
    /// </summary>
    public required int UsersWithActiveSessions { get; init; }

    /// <summary>
    /// Users holding a live refresh token. An HMAC rotation strands every one
    /// of them: their refresh stops matching, so they are signed out for real
    /// as soon as the access token they hold expires.
    /// </summary>
    public required int UsersWithActiveRefreshTokens { get; init; }

    /// <summary>
    /// Users holding a live IdP (single sign-on) session cookie, which stops
    /// being recognized at the authorize endpoint after an HMAC rotation.
    /// </summary>
    public required int UsersWithActiveIdpSessions { get; init; }

    /// <summary>
    /// Unused, unexpired password-reset links. An HMAC rotation kills every one
    /// of them — including the links of people locked out right now.
    /// </summary>
    public required int PendingPasswordResets { get; init; }

    /// <summary>
    /// Unused, unexpired two-factor challenges — sign-ins in flight at the
    /// moment of the rotation.
    /// </summary>
    public required int PendingTwoFactorChallenges { get; init; }

    /// <summary>
    /// Live webhook keys. These share the refresh-token HMAC key, so an HMAC
    /// rotation silently stops every integration's signature from validating.
    /// Not a user count, and the one consequence the old warning never mentioned.
    /// </summary>
    public required int ActiveWebhookKeys { get; init; }

    // The HMAC keyring (S11). An emergency rotation, or the retire of a key still in its
    // window, also stops every other opaque credential computed under the old keys; a
    // scheduled rotation stops nothing within the overlap window, and the two latest
    // expiries are what sizes that window (KR4).

    /// <summary>Pending, unexpired organization invitations.</summary>
    public int PendingInvitations { get; init; }

    /// <summary>Unconsumed, unexpired OAuth authorization codes.</summary>
    public int PendingAuthorizationCodes { get; init; }

    /// <summary>Unexpired parked authorize requests.</summary>
    public int PendingAuthorizationRequests { get; init; }

    /// <summary>Unconsumed, unexpired pending registrations.</summary>
    public int PendingRegistrations { get; init; }

    /// <summary>
    /// Unused, unexpired emailed codes: email verification, account deletion, ownership
    /// transfer and the two-factor bind code.
    /// </summary>
    public int PendingEmailCodes { get; init; }

    /// <summary>
    /// Users holding a live refresh token that outlives the 30-day overlap cap: idle ones
    /// sign in again after a scheduled rotation (owner decision D-S11B-2).
    /// </summary>
    public int UsersWithRefreshTokensBeyondOverlap { get; init; }

    /// <summary>Users holding a live SSO session that outlives the 30-day overlap cap.</summary>
    public int UsersWithSsoSessionsBeyondOverlap { get; init; }

    /// <summary>The latest expiry of a live refresh token, or null when there is none.</summary>
    public DateTime? LatestRefreshTokenExpiresAt { get; init; }

    /// <summary>The latest expiry of a live SSO session, or null when there is none.</summary>
    public DateTime? LatestSsoSessionExpiresAt { get; init; }
}
