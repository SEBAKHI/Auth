using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.TokenExchange;

/// <summary>
/// Redeems a one-time authorization code for tokens. The code is consumed
/// atomically (a second redemption attempt finds nothing to consume and is
/// rejected and logged as a possible replay), then the request is verified
/// against everything the code was bound to: client, redirect_uri, and the
/// PKCE challenge. Token issuance reuses the shared login response builder.
/// </summary>
public class ExchangeAuthorizationCodeCommandHandler
    : IRequestHandler<ExchangeAuthorizationCodeCommand, ErrorOr<OAuthTokenResponse>>
{
    // RFC 7636 §4.1: verifier is 43-128 base64url characters.
    private static readonly Regex CodeVerifierPattern = new(
        "^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly IAuthorizationCodeRepository _authorizationCodeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILogger<ExchangeAuthorizationCodeCommandHandler> _logger;

    public ExchangeAuthorizationCodeCommandHandler(
        IAuthorizationCodeRepository authorizationCodeRepository,
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IUserRepository userRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILoginResponseBuilder loginResponseBuilder,
        ICredentialRevocationService credentialRevocation,
        ILogger<ExchangeAuthorizationCodeCommandHandler> logger)
    {
        _authorizationCodeRepository = authorizationCodeRepository;
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _userRepository = userRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _loginResponseBuilder = loginResponseBuilder;
        _credentialRevocation = credentialRevocation;
        _logger = logger;
    }

    public async Task<ErrorOr<OAuthTokenResponse>> Handle(
        ExchangeAuthorizationCodeCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.RedirectUri) ||
            string.IsNullOrWhiteSpace(request.ClientId))
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(request.CodeVerifier) ||
            !CodeVerifierPattern.IsMatch(request.CodeVerifier))
        {
            return AuthErrors.PkceVerificationFailed;
        }

        var codeHash = _refreshTokenKeyService.ComputeTokenHash(request.Code);
        var code = await _authorizationCodeRepository.ConsumeByCodeHashAsync(codeHash, cancellationToken);

        if (code is null)
        {
            // Nothing was consumed: unknown code, or a replay of a consumed one.
            var existing = await _authorizationCodeRepository.GetByCodeHashAsync(codeHash, cancellationToken);
            if (existing is { IsConsumed: true })
            {
                _logger.LogWarning(
                    "Authorization code replay detected for client {ClientId}, user {UserId}. IP: {IpAddress}",
                    request.ClientId, existing.UserId, request.IpAddress);

                await RevokeWhatTheCodeIssuedAsync(existing, request, cancellationToken);
            }

            return AuthErrors.AuthorizationCodeInvalid;
        }

        if (code.IsExpired())
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

        var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
        if (application is null || !application.IsActive || application.Id != code.ApplicationId)
        {
            _logger.LogWarning(
                "Authorization code presented with mismatched client {ClientId}. IP: {IpAddress}",
                request.ClientId, request.IpAddress);
            return AuthErrors.InvalidClient;
        }

        if (!string.Equals(request.RedirectUri, code.RedirectUri, StringComparison.Ordinal))
        {
            return AuthErrors.InvalidRedirectUri;
        }

        if (!VerifyPkce(request.CodeVerifier, code.CodeChallenge))
        {
            _logger.LogWarning(
                "PKCE verification failed for client {ClientId}, user {UserId}. IP: {IpAddress}",
                request.ClientId, code.UserId, request.IpAddress);
            return AuthErrors.PkceVerificationFailed;
        }

        var user = await _userRepository.GetByIdAsync(code.UserId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

        if (!user.CanRenewCredentials())
        {
            return UserErrors.AccountLocked;
        }

        // Defense in depth. The authorize endpoint already refused anyone not
        // entitled; this only fires when the invitation was withdrawn, or the
        // application was closed down, in the seconds between the two calls.
        if (!await _applicationAccessRepository.IsUserEntitledAsync(user.Id, application.Id, cancellationToken))
        {
            _logger.LogWarning(
                "Access denied at token exchange for user {UserId}, client {ClientId}",
                user.Id, request.ClientId);

            return ApplicationErrors.AccessDenied;
        }

        // No browser is on this call, so no IdP session cookie is minted here —
        // the user's SSO session was already established at interactive login.
        // The token is scoped to THIS app (aud = client id) so it cannot be
        // replayed against another first-party app; the applicationId records that on
        // the refresh token so refreshes keep the same audience.
        var built = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken,
            establishIdpSession: false, audience: application.Code, applicationId: application.Id);

        if (built.IsError)
        {
            // At the concurrent session limit. The authorization code has already
            // been consumed single-use; that is correct — the code was spent, and
            // the client must start a fresh authorize round after the user frees
            // a session.
            return built.Errors;
        }

        var loginResponse = built.Value;

        // Bind the code to what it just minted. Until this line a replay has
        // nothing to revoke, which is precisely the state the old code was
        // permanently in.
        //
        // Guarded, because by this point the sign-in has fully landed: the
        // refresh token, the session row and the login record are all committed
        // and the code is spent. Letting a transient database fault throw here
        // would answer 500 with no tokens for a sign-in that actually succeeded,
        // and the client could not even retry — the code is consumed, so the
        // retry reads as a replay. Losing the stamp costs one replay detection;
        // throwing costs the whole sign-in. Every sibling write on this path is
        // guarded for the same reason.
        if (loginResponse.SessionId is Guid issuedSessionId)
        {
            try
            {
                await _authorizationCodeRepository.RecordIssuedSessionAsync(
                    code.Id, issuedSessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not bind authorization code {CodeId} to session {SessionId}; a replay of this code will have nothing to revoke",
                    code.Id, issuedSessionId);
            }
        }

        _logger.LogInformation(
            "Authorization code exchanged for tokens: client {ClientId}, user {UserId}",
            request.ClientId, user.Id);

        return new OAuthTokenResponse
        {
            AccessToken = loginResponse.Token!.AccessToken,
            ExpiresIn = loginResponse.Token.ExpiresIn,
            RefreshToken = loginResponse.Token.RefreshToken,
            RefreshExpiresIn = loginResponse.Token.RefreshExpiresIn
        };
    }

    /// <summary>
    /// RFC 7636 §4.6: challenge must equal BASE64URL(SHA256(ASCII(verifier))).
    /// Compared in fixed time to avoid leaking prefix information.
    /// </summary>
    private static bool VerifyPkce(string codeVerifier, string storedChallenge)
    {
        var computed = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var computedBytes = Encoding.ASCII.GetBytes(computed);
        var storedBytes = Encoding.ASCII.GetBytes(storedChallenge);

        return CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    /// <summary>
    /// Revokes what a replayed code produced the first time it was redeemed.
    /// </summary>
    /// <remarks>
    /// RFC 6749 4.1.2: on a repeated code the server SHOULD revoke everything
    /// previously issued from it. Detecting the replay and leaving those tokens
    /// alive is the worst of both — the theft is recorded and the thief keeps
    /// working access.
    /// <para>
    /// Which of the two callers was the thief is unknowable: whoever redeemed
    /// first holds the tokens, and that may be the attacker racing the client or
    /// the client racing the attacker. Revoking is right either way. Both parties
    /// must authenticate again, and only one of them cannot.
    /// </para>
    /// <para>
    /// Never throws into the response: the caller is answering an attack and the
    /// answer must stay a flat invalid-code either way, revealing nothing about
    /// whether anything was found to revoke.
    /// </para>
    /// </remarks>
    private async Task RevokeWhatTheCodeIssuedAsync(
        AuthorizationCode replayed,
        ExchangeAuthorizationCodeCommand request,
        CancellationToken cancellationToken)
    {
        if (replayed.IssuedSessionId is not Guid sessionId)
        {
            // The first exchange never completed, or the row predates the column.
            // Nothing was issued, so there is nothing to take back.
            _logger.LogWarning(
                "Replayed code for user {UserId} names no issued session; nothing to revoke",
                replayed.UserId);
            return;
        }

        try
        {
            await _credentialRevocation.TerminateSessionAsync(
                sessionId,
                revokedBy: null, // a system reaction, not an administrator acting now
                TokenRevocationReasons.AuthorizationCodeReplay,
                cancellationToken);

            _logger.LogWarning(
                "Revoked session {SessionId} issued from the replayed code for user {UserId}. IP: {IpAddress}",
                sessionId, replayed.UserId, request.IpAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to revoke session {SessionId} after a code replay for user {UserId}",
                sessionId, replayed.UserId);
        }
    }
}
