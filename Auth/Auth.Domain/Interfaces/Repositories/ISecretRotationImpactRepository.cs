using Auth.Domain.ReadModels.Secrets;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Reads the live credential counts used to cost a key rotation before it runs.
/// </summary>
public interface ISecretRotationImpactRepository
{
    /// <summary>
    /// Gets every rotation-impact count in a single round trip.
    /// </summary>
    /// <param name="accessTokenLifetime">
    /// How long a minted access token stays valid, including clock skew. Access
    /// tokens are stateless, so the only way to count the live ones is to count
    /// the refresh tokens minted alongside them inside this window.
    /// </param>
    /// <param name="issuedBefore">
    /// Counts only the credentials created at or before this instant among those an
    /// HMAC key change would stop (the retire of a key that stopped issuing then);
    /// null counts every live one.
    /// </param>
    /// <param name="overlapCap">
    /// How long after a scheduled HMAC rotation an old key still verifies at most:
    /// a refresh token or SSO session expiring later than that is counted as beyond it.
    /// </param>
    Task<SecretRotationImpactSnapshot> GetImpactAsync(
        TimeSpan accessTokenLifetime,
        DateTime? issuedBefore,
        TimeSpan overlapCap,
        CancellationToken cancellationToken);
}
