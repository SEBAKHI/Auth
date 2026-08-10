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
/// out, because refresh tokens are opaque and unsigned. Rotating the HMAC key
/// is the one that signs everybody out — and it also kills every password-reset
/// link, every in-flight two-factor sign-in, and every webhook key, because all
/// four are hashed with it. Rotating the gateway token invalidates no user
/// credential whatsoever; it takes the whole API offline until both processes
/// carry the new value.
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

            SecretOperation.GenerateHmacKey or SecretOperation.ImportHmacKey => new SecretRotationImpactDto
            {
                Operation = operation,
                ApprovalExpiresAt = approvalExpiresAt,
                // Everyone holding a live refresh token is signed out for real.
                AffectedUsers = snapshot.UsersWithActiveRefreshTokens,
                Details =
                [
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersSignedOut,
                        snapshot.UsersWithActiveRefreshTokens),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.UsersWithSsoSessions,
                        snapshot.UsersWithActiveIdpSessions),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.PendingPasswordResets,
                        snapshot.PendingPasswordResets),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.PendingTwoFactorChallenges,
                        snapshot.PendingTwoFactorChallenges),
                    new SecretRotationImpactItemDto(
                        SecretRotationImpactCodes.ActiveWebhookKeys,
                        snapshot.ActiveWebhookKeys)
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
