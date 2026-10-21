using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Secrets;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Turns the raw credential counts into the per-operation blast radius an
/// administrator is shown before confirming.
/// </summary>
/// <remarks>
/// The three keys break genuinely different things, and the old single warning
/// ("regenerating signing keys invalidates all existing tokens") was wrong for
/// two of them. Rotating the RSA key forces a token refresh but signs nobody
/// out, because refresh tokens are opaque and unsigned. Replacing the HMAC key at
/// once (an emergency) is the one that signs everybody out, and it also kills every
/// link, code and sign-in in flight, because all of them are fingerprinted under it;
/// since the keyring (S11), a scheduled rotation of that key breaks almost nothing.
/// Rotating the gateway token invalidates no user credential whatsoever; it takes
/// the whole API offline until both processes carry the new value.
/// </remarks>
public static class SecretRotationImpact
{
    /// <summary>
    /// Builds the impact report for one operation from a single snapshot.
    /// </summary>
    /// <param name="operation">The operation being confirmed.</param>
    /// <param name="snapshot">Live counts read in one round trip.</param>
    /// <param name="approvalExpiresAt">When the confirmation stops being spendable.</param>
    public static SecretRotationImpactDto Build(
        SecretOperation operation,
        SecretRotationImpactSnapshot snapshot,
        DateTime approvalExpiresAt) => Build(operation, action: null, snapshot, approvalExpiresAt);

    /// <summary>
    /// Builds the impact report for one operation, and for an HMAC keyring change, for
    /// the action it confirms (S11): the same operation confirms changes that break
    /// nothing (stage, discard), changes that break only what outlives the overlap
    /// window (promote, reinstate), and changes that break everything computed under
    /// the old keys (an emergency, the generate/import aliases, the retire of a key
    /// still in its window).
    /// </summary>
    /// <param name="operation">The operation being confirmed.</param>
    /// <param name="action">The keyring action it confirms; null for every other confirmation and for the aliases.</param>
    /// <param name="snapshot">Live counts read in one round trip.</param>
    /// <param name="approvalExpiresAt">When the confirmation stops being spendable.</param>
    public static SecretRotationImpactDto Build(
        SecretOperation operation,
        KeyringAction? action,
        SecretRotationImpactSnapshot snapshot,
        DateTime approvalExpiresAt) => (operation, action) switch
        {
            (_, KeyringAction.Stage or KeyringAction.StageImport or KeyringAction.DiscardNext) => new SecretRotationImpactDto
            {
                // A staged key only verifies, and a discarded one never issued: nothing
                // anyone holds stops working.
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                AffectedUsers = 0,
                Details = [],
                RequiresApiRestart = false,
                RequiresGatewayReconfiguration = false
            },

            (_, KeyringAction.Promote or KeyringAction.Reinstate) => new SecretRotationImpactDto
            {
                // The old key keeps verifying through the overlap window, so only a
                // credential that outlives the window, and is not renewed before it ends,
                // signs in again (D-S11B-2).
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                AffectedUsers = snapshot.UsersWithRefreshTokensBeyondOverlap,
                Details =
                [
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersBeyondOverlap,
                        snapshot.UsersWithRefreshTokensBeyondOverlap),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.SsoSessionsBeyondOverlap,
                        snapshot.UsersWithSsoSessionsBeyondOverlap)
                ],
                RequiresApiRestart = false,
                RequiresGatewayReconfiguration = false
            },

            (_, KeyringAction.Retire) => new SecretRotationImpactDto
            {
                // Whatever the key still verifies stops now; webhook keys are not listed,
                // because a retire is refused while any still needs the key.
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                AffectedUsers = snapshot.UsersWithActiveRefreshTokens,
                Details = EveryHmacCredential(snapshot),
                RequiresApiRestart = false,
                RequiresGatewayReconfiguration = false
            },

            (SecretOperation.GenerateHmacKey or SecretOperation.ImportHmacKey, _) => new SecretRotationImpactDto
            {
                // An emergency, or the generate/import aliases of one: everything computed
                // under the old keys stops now, except webhook keys, which keep validating
                // for 90 days and are re-fingerprinted on their next use.
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                AffectedUsers = snapshot.UsersWithActiveRefreshTokens,
                Details =
                [
                    .. EveryHmacCredential(snapshot),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.WebhookKeysInGrace,
                        snapshot.ActiveWebhookKeys)
                ],
                RequiresApiRestart = false,
                RequiresGatewayReconfiguration = false
            },

            _ => BuildForOtherKeys(operation, snapshot, approvalExpiresAt)
        };

    // Every credential class computed under the HMAC keyring, except webhook keys.
    private static IReadOnlyList<SecretRotationImpactItemDto> EveryHmacCredential(SecretRotationImpactSnapshot snapshot) =>
    [
        new(SecretRotationImpactCodes.UsersSignedOut, snapshot.UsersWithActiveRefreshTokens),
        new(SecretRotationImpactCodes.UsersWithSsoSessions, snapshot.UsersWithActiveIdpSessions),
        new(SecretRotationImpactCodes.PendingPasswordResets, snapshot.PendingPasswordResets),
        new(SecretRotationImpactCodes.PendingInvitations, snapshot.PendingInvitations),
        new(SecretRotationImpactCodes.PendingTwoFactorChallenges, snapshot.PendingTwoFactorChallenges),
        new(SecretRotationImpactCodes.PendingEmailCodes, snapshot.PendingEmailCodes),
        new(SecretRotationImpactCodes.PendingRegistrations, snapshot.PendingRegistrations),
        new(SecretRotationImpactCodes.PendingAuthorizations,
            snapshot.PendingAuthorizationCodes + snapshot.PendingAuthorizationRequests)
    ];

    // The RSA key and the gateway token: their impact does not depend on a keyring action.
    private static SecretRotationImpactDto BuildForOtherKeys(
        SecretOperation operation,
        SecretRotationImpactSnapshot snapshot,
        DateTime approvalExpiresAt) => operation switch
        {
            SecretOperation.GenerateRsaKey or SecretOperation.ImportRsaKey => new SecretRotationImpactDto
            {
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                // The blast radius is everyone with a live session: each of them
                // meets at least one rejected request before the session ends.
                AffectedUsers = snapshot.UsersWithActiveSessions,
                Details =
                [
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersWithLiveAccessTokens,
                        snapshot.UsersWithLiveAccessTokens),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersWithActiveSessions,
                        snapshot.UsersWithActiveSessions)
                ],
                RequiresApiRestart = true,
                RequiresGatewayReconfiguration = false
            },

            // Gateway token: no user credential is invalidated, but the API stops
            // accepting proxied traffic until the gateway carries the same value,
            // so everyone with a live session is locked out meanwhile.
            _ => new SecretRotationImpactDto
            {
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                AffectedUsers = snapshot.UsersWithActiveSessions,
                Details =
                [
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersWithActiveSessions,
                        snapshot.UsersWithActiveSessions)
                ],
                RequiresApiRestart = true,
                RequiresGatewayReconfiguration = true
            }
        };
}
