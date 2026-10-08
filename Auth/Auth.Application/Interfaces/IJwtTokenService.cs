using System.Security.Claims;
using Auth.Domain.Entities;
using ErrorOr;

namespace Auth.Application.Interfaces;

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
    /// <param name="authentication">
    /// How the session was authenticated and what it must still prove: written as
    /// <c>amr</c> (a JSON array, even with one value) and <c>auth_time</c> (a number)
    /// together, or neither; and <c>mfa_req</c> only while platform authority is
    /// withheld. Required: an application token passes
    /// <see cref="AccessTokenAuthentication.Unrecorded"/>.
    /// </param>
    /// <param name="sessionId">Optional stable login-session id, emitted as the "sid" claim.</param>
    /// <param name="organizationPermissions">
    /// Organization-scoped permission codes from the user's membership roles,
    /// emitted as "org_perm" claims ("{organizationId}:{code}").
    /// </param>
    /// <param name="audience">
    /// The token's intended audience (the "aud" claim). When null, the platform
    /// default is used. The authorization-code flow passes the requesting app's
    /// client id so a token minted for one app is not accepted by another.
    /// </param>
    /// <param name="scope">
    /// The scopes granted to the application, space-delimited, emitted as ONE
    /// "scope" claim (RFC 9068 §2.2.3). Null or empty for platform tokens, which
    /// carry no "scope" claim.
    /// </param>
    /// <param name="organization">
    /// The single organization to name in <c>org_id</c> and <c>org_name</c>, from
    /// <see cref="ITokenClaimsResolver"/>; null emits neither claim. Every mint
    /// site passes it, the refresh included, or the claims vanish at the first
    /// refresh.
    /// </param>
    /// <returns>The JWT access token.</returns>
    string GenerateAccessToken(
        User user,
        IEnumerable<string> permissions,
        IEnumerable<string> roles,
        AccessTokenAuthentication authentication,
        Guid? sessionId = null,
        IEnumerable<(Guid OrganizationId, string Code)>? organizationPermissions = null,
        string? audience = null,
        string? scope = null,
        TokenOrganization? organization = null);

    /// <summary>
    /// Generates a cryptographically secure random refresh token.
    /// </summary>
    /// <returns>The plain text refresh token (64 bytes, base64 encoded).</returns>
    /// <remarks>
    /// The token hash should be computed using <see cref="IRefreshTokenKeyService.ComputeTokenHash"/>
    /// for secure storage in the database.
    /// </remarks>
    string GenerateRefreshToken();

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
    /// Gets the token expiration time without full validation.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The expiration time (UTC) or null if not found.</returns>
    DateTime? GetTokenExpiry(string token);

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
}
