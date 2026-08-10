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
    Task<SecretRotationImpactSnapshot> GetImpactAsync(
        TimeSpan accessTokenLifetime,
        CancellationToken cancellationToken);
}
