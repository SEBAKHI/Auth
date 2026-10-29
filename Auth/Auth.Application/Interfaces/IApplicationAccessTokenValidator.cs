using System.Security.Claims;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Recognizes an access token this server issued to exactly one application and that is still
/// valid, by the userinfo scheme's own parameters (OI-128 PR B, introspection).
/// </summary>
/// <remarks>
/// One source: the parameters the userinfo scheme validates with on a request (issuer, signing
/// key, RS256, lifetime, a single code-shaped audience that is not the platform's, the token
/// types). A second, hand-written set would drift from it, and the drift would answer
/// <c>active</c> for a token the API itself refuses.
/// </remarks>
public interface IApplicationAccessTokenValidator
{
    /// <summary>
    /// Validates <paramref name="token"/> as an application access token.
    /// </summary>
    /// <returns>
    /// The token's claims under their original names (<c>aud</c>, <c>jti</c>, <c>sid</c>), or
    /// <c>AuthErrors.InvalidToken</c> for anything else. A bad token never throws.
    /// </returns>
    Task<ErrorOr<ClaimsPrincipal>> ValidateAsync(string token);
}
