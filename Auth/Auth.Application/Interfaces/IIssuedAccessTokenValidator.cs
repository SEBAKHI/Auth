using System.Security.Claims;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Recognizes an access token this server signed and that is still valid: one issued to the
/// platform (the console and the accounts app) or to exactly one application.
/// </summary>
/// <remarks>
/// The revocation endpoint is anonymous (RFC 7009), so holding such a token is the only thing
/// that lets a caller write to the blacklist. The answer must therefore be the bearer schemes'
/// own: a stricter rule leaves alive a token the API still accepts, and a looser one stores state
/// for a token the API never would.
/// </remarks>
public interface IIssuedAccessTokenValidator
{
    /// <summary>
    /// Validates <paramref name="token"/> as an access token for the platform audience, or for
    /// exactly one application audience.
    /// </summary>
    /// <param name="token">The compact JWT, as presented.</param>
    /// <returns>
    /// The token's claims under their original names (<c>jti</c>, <c>exp</c>, <c>sid</c>), or
    /// <c>AuthErrors.InvalidToken</c> for a forged, foreign, expired or malformed token. A bad
    /// token never throws.
    /// </returns>
    Task<ErrorOr<ClaimsPrincipal>> ValidateAsync(string token);
}
