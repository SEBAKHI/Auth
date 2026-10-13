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
    /// Mints a machine access token (OI-128) for the application whose code is
    /// <paramref name="clientId"/>: header <c>typ: at+jwt</c> (RFC 9068), RS256 under the same
    /// signing key as every other access token, and only the claims <c>iss</c>, <c>sub</c> and
    /// <c>client_id</c> (both the code), <c>aud</c> (<c>MachineClients.Audience</c>),
    /// <c>scope</c>, <c>jti</c>, <c>iat</c> and <c>exp</c>. No user, session, role, permission
    /// or organization claim. Lives <c>MachineClients.AccessTokenLifetime</c>.
    /// </summary>
    /// <param name="clientId">The application code.</param>
    /// <param name="scopes">The granted machine scopes; never empty.</param>
    string GenerateMachineAccessToken(string clientId, IReadOnlyCollection<string> scopes);

    /// <summary>
    /// Generates a cryptographically secure random opaque value. Despite the name, it mints
    /// every opaque bearer value this server issues: refresh tokens, authorization codes, the
    /// IdP session token, two-factor challenge tokens and external-login nonces.
    /// </summary>
    /// <returns>
    /// 64 lowercase hex characters (32 random bytes): no character a URL, a cookie or a
    /// firewall rule treats as special.
    /// </returns>
    /// <remarks>
    /// The token hash should be computed using <see cref="IRefreshTokenKeyService.ComputeTokenHash"/>
    /// for secure storage in the database. Callers and clients treat the value as opaque and
    /// never depend on its length or alphabet.
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
