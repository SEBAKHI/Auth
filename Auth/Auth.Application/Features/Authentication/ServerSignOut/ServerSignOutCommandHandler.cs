using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth.Application.Features.Authentication.ServerSignOut;

/// <summary>
/// Ends a person's session at a server-side application, and the single sign-on session of the
/// browser they signed in from, at that application's request (OI-183).
/// </summary>
/// <remarks>
/// <para>
/// Why no confirmation is needed here when GET end-session must ask: the proof is two things only
/// that application's server holds together. An assertion signed by its sign-in key, and the
/// refresh token of this person's session, bound to that key (OI-171). A page in a browser has
/// neither, and this endpoint reads no cookie, so there is nothing ambient for another site to
/// forge. A thief of the token without the key ends nothing; a thief of the key without the token
/// names no session.
/// </para>
/// <para>
/// The order is what keeps the endpoint from being an oracle: the client is authenticated before
/// the token is looked up, and every token that does not qualify (unknown, another application's,
/// a public client's) gets the same answer.
/// </para>
/// <para>
/// Only a live chain is acted on: the presented token, or, when it was rotated within the
/// application's replay grace, its live replacement. A stale token cannot end the IdP session a
/// later sign-in in that browser established; a live token's link follows the person into a new
/// sign-in in the same browser (RelinkIdpSessionAsync), a revoked one's does not. Nothing here
/// mints a token, so a stale presentation is answered, logged, and never treated as reuse.
/// </para>
/// <para>
/// What ends is this browser's IdP session and this application's session (owner decision D2
/// (a)). The person's other browsers keep theirs. Other applications' tokens are left as they
/// are: ending those is single logout (OI-155). The platform's own sign-ins born with the IdP
/// session end at their next refresh, as after any end of it.
/// </para>
/// </remarks>
public sealed class ServerSignOutCommandHandler : IRequestHandler<ServerSignOutCommand, ErrorOr<ServerSignOutResult>>
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerSignOutCommandHandler> _logger;

    public ServerSignOutCommandHandler(
        IClientAuthenticator clientAuthenticator,
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        TimeProvider timeProvider,
        ILogger<ServerSignOutCommandHandler> logger)
    {
        _clientAuthenticator = clientAuthenticator;
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _jwtSettings = jwtSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<ServerSignOutResult>> Handle(
        ServerSignOutCommand request, CancellationToken cancellationToken)
    {
        var client = await _clientAuthenticator.AuthenticateAsync(
            new ClientAssertionRequest(request.ClientAssertionType, request.ClientAssertion, request.ClientId, request.IpAddress),
            MachineKeyPurpose.SignIn,
            cancellationToken);
        if (client.IsError)
        {
            return client.Errors;
        }

        var applicationId = client.Value.ApplicationId;
        var presented = await _refreshTokenRepository.GetByTokenHashAsync(
            _refreshTokenKeyService.ComputeTokenHash(request.RefreshToken), cancellationToken);
        if (presented is null || !presented.IsBoundTo(applicationId))
        {
            // One answer for an unknown token, another application's, and a public client's: the
            // reason goes to the log only, so the answer tells an application nothing about a
            // token that is not its own.
            _logger.LogWarning(
                "Server sign-out by application {ApplicationId} refused: the token is {Reason}. IP: {IpAddress}",
                applicationId,
                presented is null ? "unknown" : presented.ApplicationId == applicationId ? "not bound to the key" : "another application's",
                request.IpAddress);
            return AuthErrors.InvalidToken;
        }

        var live = await FindLiveChainAsync(presented, applicationId, cancellationToken);
        if (live is null)
        {
            // Nothing is ended: the session is already over (a second tab, a repeat), or the token
            // is stale. The answer is false, and says nothing about the browser: whether its
            // sign-in lives is the end-session page's to find out from its own cookie. Answering
            // it here would tell an application, for as long as it keeps an old token, whether the
            // person is still signed in elsewhere.
            if (string.Equals(presented.ReasonRevoked, TokenRevocationReasons.SignedOutByApplication, StringComparison.Ordinal))
            {
                // A repeat of a sign-out this endpoint already made: a second tab, a retry.
                _logger.LogInformation(
                    "Server sign-out by application {ApplicationId} for user {UserId}: already signed out here; nothing to end",
                    applicationId, presented.UserId);
            }
            else
            {
                // A token rotated outside the grace, revoked for another reason, or expired: the
                // application presented a token it should no longer hold (E3).
                _logger.LogWarning(
                    "Server sign-out by application {ApplicationId} for user {UserId}: the token is stale ({Reason}); nothing ended. IP: {IpAddress}",
                    applicationId, presented.UserId, presented.IsRevoked ? presented.ReasonRevoked ?? "revoked" : "expired", request.IpAddress);
            }

            return new ServerSignOutResult { SsoSessionEnded = false };
        }

        // From the first write on, the caller cannot call it off: a server that times out and
        // falls back must not leave the sign-out half done. The IdP session goes first: it is the
        // one the application cannot reach any other way. True only when this call ended a live
        // one: a linked session already over says nothing of the browser, which may hold a newer
        // one (a sign-in after the linked session died is not relinked).
        var singleSignOnEnded = live.IdpSessionId is { } idpSessionId &&
                                await _credentialRevocation.RevokeIdpSessionByIdAsync(idpSessionId, CancellationToken.None);

        await EndApplicationSessionAsync(live);

        if (live.IdpSessionId is null)
        {
            // E1: a token issued before the link existed, or whose IdP session failed to mint.
            // The application then sends the browser to the end-session endpoint, which asks only
            // if that browser still holds a session.
            _logger.LogInformation(
                "Server sign-out by application {ApplicationId} for user {UserId}: the token names no IdP session; the application session ended alone",
                applicationId, live.UserId);
        }

        await _publisher.Publish(
            new UserSignedOutByApplicationEvent(
                Guid.NewGuid(), live.UserId, applicationId, live.SessionId, singleSignOnEnded,
                live.UserId, _timeProvider.GetUtcNow()),
            CancellationToken.None);

        return new ServerSignOutResult { SsoSessionEnded = singleSignOnEnded };
    }

    /// <summary>
    /// The token of the person's session as it stands now, or null when none is live: the
    /// presented one while it is live, else its replacement when the presented one was rotated
    /// within the application's replay grace (a sign-out racing the server's own renewal, E3).
    /// </summary>
    private async Task<RefreshTokenEntity?> FindLiveChainAsync(
        RefreshTokenEntity presented, Guid applicationId, CancellationToken cancellationToken)
    {
        if (!presented.IsRevoked)
        {
            return presented.IsExpired() ? null : presented;
        }

        var grace = _jwtSettings.ApplicationRefreshReplayGrace;
        if (!_jwtSettings.RotateRefreshTokens || grace <= TimeSpan.Zero ||
            !presented.IsWithinReplayGrace(grace, _timeProvider.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        var replacement = await _refreshTokenRepository.GetByTokenHashAsync(
            presented.ReplacedByTokenHash!, cancellationToken);

        // The same session as well: rotation keeps it, so this only guards against a replacement
        // pointer that names another session's token.
        return replacement is { IsRevoked: false } &&
               !replacement.IsExpired() &&
               replacement.UserId == presented.UserId &&
               replacement.SessionId == presented.SessionId &&
               replacement.IsBoundTo(applicationId)
            ? replacement
            : null;
    }

    /// <summary>
    /// Ends the application's session the token belongs to, as revocation does (RFC 7009 §2.1):
    /// its refresh tokens, its row, and the access tokens already handed out under it.
    /// </summary>
    private async Task EndApplicationSessionAsync(RefreshTokenEntity live)
    {
        if (live.SessionId is { } sessionId)
        {
            await _credentialRevocation.TerminateSessionAsync(
                sessionId, live.UserId, TokenRevocationReasons.SignedOutByApplication, CancellationToken.None);
            return;
        }

        // A session-less (legacy) token belongs to no grant beyond itself.
        live.Revoke(live.UserId, TokenRevocationReasons.SignedOutByApplication);
        await _refreshTokenRepository.UpdateAsync(live, CancellationToken.None);
    }
}
