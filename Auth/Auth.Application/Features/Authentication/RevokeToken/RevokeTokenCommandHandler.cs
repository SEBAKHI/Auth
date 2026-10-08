using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.RevokeToken;

/// <summary>
/// Handler for the revoke token command.
/// </summary>
public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, ErrorOr<Success>>
{
    private readonly IIssuedAccessTokenValidator _accessTokenValidator;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IOptionsMonitor<JwtSettings> _jwtSettings;
    private readonly ILogger<RevokeTokenCommandHandler> _logger;

    public RevokeTokenCommandHandler(
        IIssuedAccessTokenValidator accessTokenValidator,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ICredentialRevocationService credentialRevocation,
        IOptionsMonitor<JwtSettings> jwtSettings,
        ILogger<RevokeTokenCommandHandler> logger)
    {
        _accessTokenValidator = accessTokenValidator;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _credentialRevocation = credentialRevocation;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return AuthErrors.InvalidToken;
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

        // RFC 7009 2.1: the hint only says where to look first. A token not found
        // under it is looked up under the other type, so a client that mislabels
        // its refresh token at sign-out still ends its session.
        ErrorOr<bool> found = tokenType == TokenTypeHint.AccessToken
            ? await TryRevokeAccessTokenAsync(request.Token)
            : await TryRevokeRefreshTokenAsync(request.Token, request.RevokedBy, cancellationToken);
        if (found.IsError)
        {
            return found.Errors;
        }

        if (!found.Value)
        {
            if (tokenType == TokenTypeHint.AccessToken)
            {
                found = await TryRevokeRefreshTokenAsync(request.Token, request.RevokedBy, cancellationToken);
            }
            else if (request.Token.Contains('.'))
            {
                // Only a JWT can be an access token; anything else skips the validator.
                found = await TryRevokeAccessTokenAsync(request.Token);
            }

            if (found.IsError)
            {
                return found.Errors;
            }
        }

        if (!found.Value)
        {
            // RFC 7009 2.2: "the authorization server responds with HTTP status
            // code 200 if the token has been revoked successfully OR IF THE CLIENT
            // SUBMITTED AN INVALID TOKEN." Answering an error would tell an
            // anonymous caller whether a token was real — turning the revocation
            // endpoint into an oracle for guessing valid tokens. Nothing was
            // revoked because there was nothing to revoke, which is the outcome
            // the caller asked for.
            _logger.LogInformation(
                "Revocation requested for a token that is neither a valid access token nor a stored refresh token; nothing to revoke");
        }

        return Result.Success;
    }

    /// <summary>
    /// Blacklists the token when it is an access token this server signed.
    /// </summary>
    /// <returns>True when the token is one of ours (revoked now or already); false when it is not.</returns>
    private async Task<ErrorOr<bool>> TryRevokeAccessTokenAsync(string token)
    {
        // Validated under the bearer schemes' own rules, so an application's token
        // is recognized as well as a console token. The platform-only check that
        // stood here answered an application's token 200 and left it working.
        var validationResult = await _accessTokenValidator.ValidateAsync(token);

        if (validationResult.IsError)
        {
            // Nothing is stored for a token this process did not sign. A
            // fallback used to live here that read the UNVERIFIED JWT anyway
            // and pinned its jti in memory for 24 hours — which made this
            // anonymous endpoint a memory sink: any caller could mint an
            // unsigned token carrying a 250 KB jti (the parser's size limit)
            // and park it here at the edge's twenty requests a minute per
            // address, for a day, with nothing to evict it but the clock. A
            // token that fails validation is forged (nothing of ours to
            // revoke), expired (already dead) or malformed — in every case the
            // correct amount of state to keep is zero.
            return false;
        }

        var claims = validationResult.Value;
        var tokenId = claims.FindFirst("jti")?.Value;
        var expClaim = claims.FindFirst("exp")?.Value;

        if (string.IsNullOrEmpty(tokenId))
        {
            return AuthErrors.InvalidToken;
        }

        // Revoked already: the same answer, and no second write. Every write is a
        // durable row, and anyone holding a valid token can call this anonymously.
        if (_tokenBlacklistService.IsTokenBlacklisted(tokenId))
        {
            return true;
        }

        // Held until the last moment the token could still be accepted: its exp
        // plus the clock skew validation allows on top of it, from the live
        // settings — the same rule as a killed session's id. Ending at exp alone
        // let a cleanup or a recycle in that margin accept the token again.
        var expiresAt = DateTime.UtcNow.AddHours(1); // Default
        if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime + _jwtSettings.CurrentValue.ClockSkew;
        }

        // This token and nothing more. Its session is left running on purpose:
        // RFC 7009 makes that optional, and a leaked access token must not be
        // enough to sign its owner out.
        _tokenBlacklistService.BlacklistToken(tokenId, expiresAt);
        _logger.LogInformation("Revoked access token with JTI: {Jti}", tokenId);

        return true;
    }

    /// <summary>
    /// Revokes the refresh token, and its whole session when it has one.
    /// </summary>
    /// <returns>True when a stored refresh token matched (revoked now or already); false when none did.</returns>
    private async Task<bool> TryRevokeRefreshTokenAsync(
        string token,
        Guid? revokedBy,
        CancellationToken cancellationToken)
    {
        // Compute hash and lookup by hash
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(token);
        var refreshToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (refreshToken == null)
        {
            return false;
        }

        if (refreshToken.IsRevoked)
        {
            // Already revoked
            return true;
        }

        // From the first write on, the caller cannot call it off: a client that
        // signs out and hangs up (a fire-and-forget sign-out does) must not leave
        // the revocation half done.
        if (refreshToken.SessionId is { } sessionId)
        {
            // RFC 7009 2.1: revoking a refresh token SHOULD also invalidate the
            // access tokens of the same grant. The session is that grant. Ending
            // it revokes every refresh token issued under it, this one included,
            // and blacklists its id, so the access token already handed out stops
            // working now instead of at its expiry.
            await _credentialRevocation.TerminateSessionAsync(
                sessionId, revokedBy, TokenRevocationReasons.RevocationRequested, CancellationToken.None);

            _logger.LogInformation(
                "Revoked refresh token for user {UserId} and ended its session {SessionId}",
                refreshToken.UserId, sessionId);

            return true;
        }

        // A session-less (legacy) token belongs to no grant beyond itself.
        refreshToken.Revoke(revokedBy, TokenRevocationReasons.RevocationRequested);
        await _refreshTokenRepository.UpdateAsync(refreshToken, CancellationToken.None);

        _logger.LogInformation(
            "Revoked refresh token for user {UserId}",
            refreshToken.UserId);

        return true;
    }
}
