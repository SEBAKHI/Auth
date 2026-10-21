using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.TokenExchange;

/// <summary>
/// Redeems a one-time authorization code for tokens. The code is consumed
/// atomically (a second redemption attempt finds nothing to consume and is
/// rejected and logged as a possible replay), then the request is verified
/// against everything the code was bound to: client, redirect_uri, and the
/// PKCE challenge. Token issuance reuses the shared login response builder.
/// </summary>
/// <remarks>
/// <para>
/// A server-side application authenticates with its sign-in key (OI-171,
/// <c>private_key_jwt</c>), BEFORE anything is consumed: a request whose
/// assertion does not authenticate leaves the code to its rightful client. Who
/// may consume a code is part of the consume statement itself. A request that
/// did not authenticate consumes public codes only, so a code issued to a
/// server-side redirect URI stays the server's; an authenticated one consumes
/// only its own application's codes.
/// </para>
/// <para>
/// Only an authenticated replay of a server-side code revokes what it issued: an
/// unauthenticated replay answers <c>Auth.InvalidClient</c> and changes nothing,
/// so seeing a redeemed code is no way to sign its person out of the server's
/// session. A public code redeemed WITH a valid assertion is accepted and its
/// refresh token is bound (the upgrade path of a server that started public);
/// with an assertion that does not authenticate it is refused, and not consumed.
/// PKCE stays mandatory on every code.
/// </para>
/// <para>
/// The sign-in is recorded where the person is (OI-178). An authenticated caller is
/// a server redeeming the code on the person's behalf, so the session, the history,
/// the location and the new-device alert come from the browser request that produced
/// the code (<see cref="AuthorizationCode.OriginFor"/>), never from the server's own
/// request; the server's address stays in the log. A public client's request is the
/// browser itself and is recorded as before.
/// </para>
/// </remarks>
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
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly ILogger<ExchangeAuthorizationCodeCommandHandler> _logger;

    public ExchangeAuthorizationCodeCommandHandler(
        IAuthorizationCodeRepository authorizationCodeRepository,
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IUserRepository userRepository,
        IIdpSessionRepository idpSessionRepository,
        ICredentialHasher credentialHasher,
        ILoginResponseBuilder loginResponseBuilder,
        ICredentialRevocationService credentialRevocation,
        IClientAuthenticator clientAuthenticator,
        ILogger<ExchangeAuthorizationCodeCommandHandler> logger)
    {
        _authorizationCodeRepository = authorizationCodeRepository;
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _userRepository = userRepository;
        _idpSessionRepository = idpSessionRepository;
        _credentialHasher = credentialHasher;
        _loginResponseBuilder = loginResponseBuilder;
        _credentialRevocation = credentialRevocation;
        _clientAuthenticator = clientAuthenticator;
        _logger = logger;
    }

    public async Task<ErrorOr<OAuthTokenResponse>> Handle(
        ExchangeAuthorizationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var assertion = new ClientAssertionRequest(
            request.ClientAssertionType, request.ClientAssertion, request.ClientId, request.IpAddress);

        // A public client names itself with client_id; an authenticated one may leave it out, and
        // its key names its application (OI-171, CA4).
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.RedirectUri) ||
            (string.IsNullOrWhiteSpace(request.ClientId) && !assertion.IsPresent))
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(request.CodeVerifier) ||
            !CodeVerifierPattern.IsMatch(request.CodeVerifier))
        {
            return AuthErrors.PkceVerificationFailed;
        }

        // The client is authenticated before anything is consumed (OI-171, CA1 and CA2).
        AuthenticatedClient? client = null;
        if (assertion.IsPresent)
        {
            var authenticated = await _clientAuthenticator.AuthenticateAsync(
                assertion, MachineKeyPurpose.SignIn, cancellationToken);
            if (authenticated.IsError)
            {
                return authenticated.Errors;
            }

            client = authenticated.Value;
        }

        var clientId = request.ClientId ?? client!.ApplicationCode;
        var candidates = _credentialHasher.Candidates(CredentialPurpose.AuthorizationCode, request.Code);
        var code = client is null
            ? await _authorizationCodeRepository.ConsumeByCodeHashAsync(candidates, cancellationToken)
            : await _authorizationCodeRepository.ConsumeForClientAsync(candidates, client.ApplicationId, cancellationToken);

        if (code is null)
        {
            // Nothing was consumed: an unknown code, a replay of a consumed one, or a code this
            // request may not consume (another application's, or a server-side one unauthenticated).
            var existing = await _authorizationCodeRepository.GetByCodeHashAsync(candidates, cancellationToken);
            if (existing is not null && client is not null && existing.ApplicationId != client.ApplicationId)
            {
                _logger.LogWarning(
                    "Authorization code of application {CodeApplicationId} presented with a key of application {KeyApplicationId}. IP: {IpAddress}",
                    existing.ApplicationId, client.ApplicationId, request.IpAddress);
                return AuthErrors.InvalidClient;
            }

            if (existing is { RequiresClientAuthentication: true } && client is null)
            {
                // Left unconsumed, and a replay of it revokes nothing: only the server that
                // holds the key may redeem it or end what it issued (CA2b).
                _logger.LogWarning(
                    "Authorization code issued to a server-side redirect URI of application {ApplicationId} presented without client authentication. IP: {IpAddress}",
                    existing.ApplicationId, request.IpAddress);
                return AuthErrors.InvalidClient;
            }

            if (existing is { IsConsumed: true })
            {
                _logger.LogWarning(
                    "Authorization code replay detected for client {ClientId}, user {UserId}. IP: {IpAddress}",
                    clientId, existing.UserId, request.IpAddress);

                await RevokeWhatTheCodeIssuedAsync(existing, request, cancellationToken);
            }

            return AuthErrors.AuthorizationCodeInvalid;
        }

        // Backstop: the public consume statement never hands out a server-side code, so this
        // holds by construction. Re-checked here so that a regression in that one SQL clause, or
        // in the read behind it, is refused rather than issuing an unbound session from a code
        // only the server may redeem. The code stays consumed: it was spent by a caller that
        // should never have held it.
        if (code.RequiresClientAuthentication && client is null)
        {
            _logger.LogError(
                "A code issued to a server-side redirect URI of application {ApplicationId} was consumed without client authentication; nothing is issued. IP: {IpAddress}",
                code.ApplicationId, request.IpAddress);
            return AuthErrors.InvalidClient;
        }

        if (code.IsExpired())
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

        var application = await _applicationRepository.GetByCodeAsync(clientId, cancellationToken);
        if (application is null || !application.IsActive || application.Id != code.ApplicationId)
        {
            _logger.LogWarning(
                "Authorization code presented with mismatched client {ClientId}. IP: {IpAddress}",
                clientId, request.IpAddress);
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
                clientId, code.UserId, request.IpAddress);
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
                user.Id, clientId);

            return ApplicationErrors.AccessDenied;
        }

        // The browser's SSO session the code came from must still stand (OI-155, D-84-1 (a)): a
        // sign-out in that browser ends every sign-in born from it, and a code issued just before
        // and redeemed just after would otherwise start one the sign-out never saw. Revoked or
        // missing is refused; the code stays consumed. A code issued before codes recorded their
        // session (OI-154) carries none and is redeemed as before.
        if (code.IdpSessionId is { } idpSessionId &&
            await _idpSessionRepository.GetByIdAsync(idpSessionId, cancellationToken) is not { IsRevoked: false })
        {
            _logger.LogWarning(
                "Authorization code of application {ApplicationId} for user {UserId} redeemed after its IdP session {IdpSessionId} ended; nothing is issued",
                application.Id, user.Id, idpSessionId);
            return AuthErrors.AuthorizationCodeInvalid;
        }

        // No browser is on this call, so no IdP session cookie is minted here —
        // the user's SSO session was already established at interactive login.
        // The token is scoped to THIS app (aud = client id) so it cannot be
        // replayed against another first-party app; the applicationId records that on
        // the refresh token so refreshes keep the same audience. The grant is the
        // one recorded with the code at authorize, narrowed to what the
        // application is allowed now, like a refresh: an administrator who removes
        // a scope between the two calls is honoured here, as the entitlement
        // re-check above is. A code minted before scopes existed is openid only.
        var grantedScopes = code.GrantedScopes.Intersect(application.AllowedScopes);
        // Where the person is: the browser that asked for the code, when a server
        // redeems it; this request, when the browser does (OI-178).
        var origin = code.OriginFor(
            new SignInOrigin(request.IpAddress, request.UserAgent, request.DeviceId),
            clientAuthenticated: client is not null);
        // Unknown: the code does not carry how the IdP session behind it signed in
        // yet, and an application token carries no platform authority to protect.
        // A renewal proves nothing about the password, so it lifts no lock (G8).
        var built = await _loginResponseBuilder.BuildAsync(
            user, origin.IpAddress, origin.UserAgent, origin.DeviceId, AuthenticationMethods.Unknown,
            passwordProven: false, cancellationToken,
            establishIdpSession: false, audience: application.Code, applicationId: application.Id,
            scope: grantedScopes.Value,
            // The IdP session the code was issued from: the application's refreshes
            // then count as its use (OI-154, D-154-2 (b)).
            linkedIdpSessionId: code.IdpSessionId,
            // An authenticated exchange binds its refresh token to the application's key
            // (OI-171): a code issued to a server-side URI is never redeemed otherwise, and a
            // public code redeemed with a valid assertion upgrades its chain (CA5).
            requiresClientAuthentication: client is not null);

        if (built.IsError)
        {
            // At the concurrent session limit. The authorization code has already
            // been consumed single-use; that is correct — the code was spent, and
            // the client must start a fresh authorize round after the user frees
            // a session.
            return built.Errors;
        }

        var loginResponse = built.Value;

        if (await IdpSessionEndedMeanwhileAsync(code, loginResponse, user.Id, application.Id))
        {
            return AuthErrors.AuthorizationCodeInvalid;
        }

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
            "Authorization code exchanged for tokens: client {ClientId}, user {UserId}, client authenticated {ClientAuthenticated}, via {TokenRequestIp}",
            clientId, user.Id, client is not null, request.IpAddress);

        return new OAuthTokenResponse
        {
            AccessToken = loginResponse.Token!.AccessToken,
            ExpiresIn = loginResponse.Token.ExpiresIn,
            RefreshToken = loginResponse.Token.RefreshToken,
            RefreshExpiresIn = loginResponse.Token.RefreshExpiresIn,
            Scope = grantedScopes.Value
        };
    }

    /// <summary>
    /// The second read of the code's IdP session, after the sign-in it issued is written (OI-155).
    /// The first read ran before the refresh token existed, so a sign-out that committed in between
    /// read the browser's chains without this one. Write, then read, on both sides: each statement
    /// commits on its own (no transaction spans this handler), the sign-out revokes the IdP session
    /// and only then reads its chains, and this handler writes the chain and only then reads the
    /// IdP session. A sign-out this read does not see committed after the chain was written, so its
    /// reads find the chain; one this read sees, this handler answers itself, by ending the session
    /// it just issued. A failed read is logged and the sign-in stands: it has fully landed, and the
    /// code is spent.
    /// </summary>
    /// <returns>True when the IdP session ended meanwhile and the issued session was ended.</returns>
    private async Task<bool> IdpSessionEndedMeanwhileAsync(
        AuthorizationCode code, LoginResponse loginResponse, Guid userId, Guid applicationId)
    {
        if (code.IdpSessionId is not { } idpSessionId)
        {
            return false;
        }

        IdpSession? idpSession;
        try
        {
            // Without the request's token: the chain is written, and a client that leaves now
            // must not keep it alive past a sign-out.
            idpSession = await _idpSessionRepository.GetByIdAsync(idpSessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not re-read IdP session {IdpSessionId} after a code of application {ApplicationId} was redeemed for user {UserId}; the sign-in stands",
                idpSessionId, applicationId, userId);
            return false;
        }

        if (idpSession is { IsRevoked: false })
        {
            return false;
        }

        if (loginResponse.SessionId is Guid issuedSessionId)
        {
            try
            {
                await _credentialRevocation.TerminateSessionAsync(
                    issuedSessionId, userId, TokenRevocationReasons.SingleLogout, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Nothing is returned, so no one holds the refresh token it just wrote.
                _logger.LogError(ex,
                    "Session {SessionId} issued from ended IdP session {IdpSessionId} could not be ended", issuedSessionId, idpSessionId);
            }
        }

        _logger.LogWarning(
            "IdP session {IdpSessionId} of user {UserId} ended while application {ApplicationId} redeemed a code from it; the session just issued is ended and nothing is returned",
            idpSessionId, userId, applicationId);
        return true;
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
