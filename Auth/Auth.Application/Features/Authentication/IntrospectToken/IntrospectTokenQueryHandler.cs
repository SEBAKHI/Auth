using Auth.Application.Interfaces;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using System.Security.Claims;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Authentication.IntrospectToken;

/// <summary>
/// Handler for the introspect token query: an application's server asks about a token, and hears
/// <c>active: true</c> only for its own (OI-128 PR B, owner decision D-128-6 (a)).
/// </summary>
/// <remarks>
/// <para>
/// Active only for an access token issued to the caller (its single audience is the caller's
/// code, by the userinfo scheme's own validation) that no revocation reaches: the token's
/// <c>jti</c>, its session, and the person's sign-out of everything, the checks this platform's
/// own API applies; or for a refresh token of the caller's that is neither revoked nor expired.
/// Every other token, a platform token included, is <c>{"active": false}</c> (RFC 7662 §2.2), so
/// the answer tells a caller nothing about tokens that are not its own.
/// </para>
/// <para>
/// The answer for an access token carries what the token itself carries; nothing is read from
/// the user's row.
/// </para>
/// </remarks>
public class IntrospectTokenQueryHandler : IRequestHandler<IntrospectTokenQuery, ErrorOr<IntrospectTokenResponse>>
{
    private readonly IApplicationAccessTokenValidator _applicationTokens;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ILogger<IntrospectTokenQueryHandler> _logger;

    public IntrospectTokenQueryHandler(
        IApplicationAccessTokenValidator applicationTokens,
        IApplicationRepository applicationRepository,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenRepository refreshTokenRepository,
        ICredentialHasher credentialHasher,
        ILogger<IntrospectTokenQueryHandler> logger)
    {
        _applicationTokens = applicationTokens;
        _applicationRepository = applicationRepository;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenRepository = refreshTokenRepository;
        _credentialHasher = credentialHasher;
        _logger = logger;
    }

    private static IntrospectTokenResponse Inactive => new() { Active = false };

    public async Task<ErrorOr<IntrospectTokenResponse>> Handle(
        IntrospectTokenQuery request,
        CancellationToken cancellationToken)
    {
        var caller = await request.Caller.ResolveAsync(
            _applicationRepository, MachineClients.Scopes.TokensIntrospect, cancellationToken);
        if (caller.IsError)
        {
            return caller.Errors;
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Inactive;
        }

        var tokenType = request.TokenTypeHint;

        // Try to determine token type if not hinted
        if (tokenType == null)
        {
            // JWT tokens contain dots
            tokenType = request.Token.Contains('.')
                ? TokenTypeHint.AccessToken
                : TokenTypeHint.RefreshToken;
        }

        if (tokenType == TokenTypeHint.AccessToken)
        {
            return await IntrospectAccessTokenAsync(caller.Value, request.Token);
        }
        else
        {
            return await IntrospectRefreshTokenAsync(caller.Value, request.Token, cancellationToken);
        }
    }

    private async Task<ErrorOr<IntrospectTokenResponse>> IntrospectAccessTokenAsync(AppEntity caller, string token)
    {
        var validationResult = await _applicationTokens.ValidateAsync(token);
        if (validationResult.IsError)
        {
            _logger.LogDebug("Token introspection - not an application access token");
            return Inactive;
        }

        var claims = validationResult.Value;

        // The validation allows exactly one audience: issued to the caller, or not its business.
        // Codes are compared as the server compares them, in upper case (Application.Code).
        var audience = GetClaimValue(claims, JwtClaimNames.Audience);
        if (audience is null
            || !string.Equals(audience.ToUpperInvariant(), caller.Code.ToUpperInvariant(), StringComparison.Ordinal))
        {
            _logger.LogDebug("Token introspection - application {ApplicationId} asked about another audience's token", caller.Id);
            return Inactive;
        }

        // Check if blacklisted
        var jti = GetClaimValue(claims, JwtClaimNames.JwtId);
        if (!string.IsNullOrEmpty(jti) && _tokenBlacklistService.IsTokenBlacklisted(jti))
        {
            _logger.LogDebug("Token introspection - token is blacklisted: {Jti}", jti);
            return Inactive;
        }

        // A terminated session's access tokens (a sign-out, a session ended from the list, a
        // single logout) are refused by this platform's own API from that moment; the
        // application must hear the same, not wait for the token to expire.
        var sid = GetClaimValue(claims, JwtClaimNames.Sid);
        if (!string.IsNullOrEmpty(sid) && _tokenBlacklistService.IsSessionBlacklisted(sid))
        {
            _logger.LogDebug("Token introspection - session is revoked: {SessionId}", sid);
            return Inactive;
        }

        // Check user-level revocation
        var subClaim = GetClaimValue(claims, JwtClaimNames.Subject);
        var iatClaim = GetClaimValue(claims, JwtClaimNames.IssuedAt);
        if (Guid.TryParse(subClaim, out var userId) &&
            long.TryParse(iatClaim, out var iatUnix))
        {
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
            if (_tokenBlacklistService.AreUserTokensBlacklisted(userId, issuedAt))
            {
                _logger.LogDebug("Token introspection - user tokens are revoked for user: {UserId}", userId);
                return Inactive;
            }
        }

        return new IntrospectTokenResponse
        {
            Active = true,
            TokenType = "bearer",
            Scope = GetClaimValue(claims, JwtClaimNames.Scope),
            ClientId = audience,
            Sub = subClaim,
            Jti = jti,
            Iss = GetClaimValue(claims, JwtClaimNames.Issuer),
            Aud = audience,
            Email = GetClaimValue(claims, JwtClaimNames.Email),
            Username = GetClaimValue(claims, JwtClaimNames.Email),
            Exp = GetLongClaimValue(claims, JwtClaimNames.Expiration),
            Iat = GetLongClaimValue(claims, JwtClaimNames.IssuedAt),
            Nbf = GetLongClaimValue(claims, "nbf"),
            Roles = claims.FindAll(JwtClaimNames.Roles).Select(c => c.Value).ToList(),
            Permissions = claims.FindAll(JwtClaimNames.Permissions).Select(c => c.Value).ToList()
        };
    }

    private async Task<ErrorOr<IntrospectTokenResponse>> IntrospectRefreshTokenAsync(
        AppEntity caller,
        string token,
        CancellationToken cancellationToken)
    {
        // Look the token up by every fingerprint it may be stored under
        var candidates = _credentialHasher.Candidates(CredentialPurpose.RefreshToken, token);
        var refreshToken = await _refreshTokenRepository.GetByTokenHashAsync(candidates, cancellationToken);

        if (refreshToken == null)
        {
            _logger.LogDebug("Token introspection - refresh token not found");
            return Inactive;
        }

        // Issued to the caller, or not its business: a platform refresh token has no application.
        if (refreshToken.ApplicationId != caller.Id)
        {
            _logger.LogDebug("Token introspection - application {ApplicationId} asked about another client's refresh token", caller.Id);
            return Inactive;
        }

        // Check if active (not revoked and not expired)
        if (refreshToken.IsRevoked || refreshToken.IsExpired())
        {
            _logger.LogDebug("Token introspection - refresh token is not active");
            return Inactive;
        }

        return new IntrospectTokenResponse
        {
            Active = true,
            TokenType = "refresh_token",
            ClientId = caller.Code,
            Sub = refreshToken.UserId.ToString(),
            Exp = new DateTimeOffset(refreshToken.ExpiresAt).ToUnixTimeSeconds(),
            Iat = new DateTimeOffset(refreshToken.CreatedAt).ToUnixTimeSeconds()
        };
    }

    private static string? GetClaimValue(ClaimsPrincipal claims, string claimType)
    {
        return claims.FindFirst(claimType)?.Value;
    }

    private static long? GetLongClaimValue(ClaimsPrincipal claims, string claimType)
    {
        var value = claims.FindFirst(claimType)?.Value;
        if (!string.IsNullOrEmpty(value) && long.TryParse(value, out var result))
        {
            return result;
        }
        return null;
    }
}
