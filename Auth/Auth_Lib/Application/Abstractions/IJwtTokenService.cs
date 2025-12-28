using System.Security.Claims;
using Auth_Lib.Domain.Entities;
using ErrorOr;
using Microsoft.IdentityModel.Tokens;

namespace Auth_Lib.Application.Abstractions;

/// <summary>
/// Interface for JWT token generation and validation.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates an access token for a user.
    /// </summary>
    /// <param name="user">The user to generate a token for.</param>
    /// <param name="permissions">The user's effective permissions.</param>
    /// <param name="roles">The user's roles.</param>
    /// <returns>The JWT access token.</returns>
    string GenerateAccessToken(User user, IEnumerable<string> permissions, IEnumerable<string> roles);

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    /// <returns>A tuple containing the plain token and its SHA256 hash.</returns>
    (string Token, string TokenHash) GenerateRefreshToken();

    /// <summary>
    /// Validates an access token and extracts its claims.
    /// </summary>
    /// <param name="token">The JWT token to validate.</param>
    /// <returns>The claims principal or an error.</returns>
    ErrorOr<ClaimsPrincipal> ValidateAccessToken(string token);

    /// <summary>
    /// Gets the token ID (jti) from a token without full validation.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The token ID or null if not found.</returns>
    string? GetTokenId(string token);

    /// <summary>
    /// Gets the user ID from a token without full validation.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The user ID or null if not found.</returns>
    Guid? GetUserId(string token);

    /// <summary>
    /// Gets the JWKS (JSON Web Key Set) for public key distribution.
    /// </summary>
    /// <returns>The JWKS as a JSON string.</returns>
    string GetJwks();

    /// <summary>
    /// Gets the public key in PEM format.
    /// </summary>
    /// <returns>The public key PEM string.</returns>
    string GetPublicKeyPem();

    /// <summary>
    /// Gets the security key used for token signing and validation.
    /// </summary>
    /// <returns>The RSA security key.</returns>
    SecurityKey GetSecurityKey();
}
