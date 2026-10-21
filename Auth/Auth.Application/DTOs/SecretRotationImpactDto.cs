using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// What a confirmed secret operation is about to cost, returned only after the
/// administrator has proved control of their mailbox. This is the last thing
/// shown before the operation runs, so it states the blast radius in people
/// rather than in rows, and admits the parts that are not a user count at all.
/// </summary>
public record SecretRotationImpactDto
{
    /// <summary>
    /// The operation these figures describe.
    /// </summary>
    public required SecretOperation Operation { get; init; }

    /// <summary>
    /// UTC instant after which the confirmation can no longer be spent. The
    /// administrator must finish inside this window or start again.
    /// </summary>
    public required DateTime ApprovalExpiresAt { get; init; }

    /// <summary>
    /// The headline number: how many people this operation reaches. Zero is a
    /// real answer for the gateway token on an idle platform, and for any
    /// rotation performed with nobody signed in.
    /// </summary>
    public required int AffectedUsers { get; init; }

    /// <summary>
    /// The consequences that apply to this operation, each a named count. Only
    /// the entries that mean something for the chosen operation are present —
    /// telling an administrator that an RSA rotation kills password-reset links
    /// would be false.
    /// </summary>
    public required IReadOnlyList<SecretRotationImpactItemDto> Details { get; init; }

    /// <summary>
    /// Whether the rotation only takes effect once the API process restarts: the
    /// running process captured the old key at boot, so nothing changes for users
    /// until it is recycled, and the counts above are what happens then. False for
    /// a change of the HMAC keyring (S11), which this process applies the moment it
    /// is saved; another worker of an overlapped recycle catches up when it restarts.
    /// </summary>
    public required bool RequiresApiRestart { get; init; }

    /// <summary>
    /// Whether the API Gateway must be reconfigured with the same value. When
    /// true, the two processes reject each other's traffic until both carry the
    /// new token, which is an outage rather than a credential loss.
    /// </summary>
    public required bool RequiresGatewayReconfiguration { get; init; }
}

/// <summary>
/// One named consequence of a rotation and how much of it there is.
/// </summary>
/// <param name="Code">
/// Stable identifier of the consequence, matched to display copy by the client.
/// </param>
/// <param name="Count">How many of that thing the rotation breaks.</param>
public record SecretRotationImpactItemDto(string Code, int Count);

/// <summary>
/// The stable consequence codes carried by <see cref="SecretRotationImpactItemDto"/>.
/// Clients switch on these; changing one is a breaking API change.
/// </summary>
public static class SecretRotationImpactCodes
{
    /// <summary>Users whose access token is still inside its lifetime.</summary>
    public const string UsersWithLiveAccessTokens = "usersWithLiveAccessTokens";

    /// <summary>Users holding a session that has neither ended nor expired.</summary>
    public const string UsersWithActiveSessions = "usersWithActiveSessions";

    /// <summary>Users who will be signed out for real once their access token expires.</summary>
    public const string UsersSignedOut = "usersSignedOut";

    /// <summary>Users whose single sign-on session cookie stops being recognized.</summary>
    public const string UsersWithSsoSessions = "usersWithSsoSessions";

    /// <summary>Emailed password-reset links that stop working.</summary>
    public const string PendingPasswordResets = "pendingPasswordResets";

    /// <summary>Two-factor sign-ins in flight that will fail.</summary>
    public const string PendingTwoFactorChallenges = "pendingTwoFactorChallenges";

    /// <summary>Webhook keys whose signatures stop validating.</summary>
    public const string ActiveWebhookKeys = "activeWebhookKeys";

    // The HMAC keyring (S11).

    /// <summary>Webhook keys that keep validating for 90 days under the old key, then need re-issuing unless used.</summary>
    public const string WebhookKeysInGrace = "webhookKeysInGrace";

    /// <summary>Organization invitation links that stop working.</summary>
    public const string PendingInvitations = "pendingInvitations";

    /// <summary>Emailed codes in flight (verification, deletion, ownership transfer, two-factor bind) that stop working.</summary>
    public const string PendingEmailCodes = "pendingEmailCodes";

    /// <summary>Registrations waiting for their emailed code that must start again.</summary>
    public const string PendingRegistrations = "pendingRegistrations";

    /// <summary>Sign-ins to client applications in flight (authorization codes and parked requests) that fail.</summary>
    public const string PendingAuthorizations = "pendingAuthorizations";

    /// <summary>Users whose refresh token outlives the overlap window: if they stay idle past it, they sign in again.</summary>
    public const string UsersBeyondOverlap = "usersBeyondOverlap";

    /// <summary>Users whose single sign-on session outlives the overlap window and then ends.</summary>
    public const string SsoSessionsBeyondOverlap = "ssoSessionsBeyondOverlap";
}
