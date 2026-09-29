using Auth.Application.Interfaces;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.LogoutWithRefreshCookie;

/// <summary>
/// Ends the session behind a first-party refresh cookie, through the same
/// revocation the bearer sign-out uses: <see cref="ICredentialRevocationService.TerminateSessionAsync"/>
/// ends the session row, revokes every refresh token issued under it and
/// blacklists its id; the SSO session of this browser goes with it.
/// </summary>
public class LogoutWithRefreshCookieCommandHandler
    : IRequestHandler<LogoutWithRefreshCookieCommand, ErrorOr<LogoutWithRefreshCookieResult>>
{
    private const string Reason = "logout";

    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly ILogger<LogoutWithRefreshCookieCommandHandler> _logger;

    public LogoutWithRefreshCookieCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        ILogger<LogoutWithRefreshCookieCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<LogoutWithRefreshCookieResult>> Handle(
        LogoutWithRefreshCookieCommand request,
        CancellationToken cancellationToken)
    {
        var token = await _refreshTokenRepository.GetByTokenHashAsync(
            _refreshTokenKeyService.ComputeTokenHash(request.RefreshToken), cancellationToken);

        // Unknown to the server: there is nothing left to end, and the cookie is dead.
        if (token is null)
        {
            return new LogoutWithRefreshCookieResult(Ended: true);
        }

        // A new sign-in replaced the cookie after the sign-out the browser meant.
        // Ending THIS session would sign out the person now using the browser.
        if (request.ExpectedSessionId is { } expected && token.SessionId != expected)
        {
            _logger.LogInformation(
                "Cookie sign-out for user {UserId} skipped: the cookie now belongs to another session", token.UserId);
            return new LogoutWithRefreshCookieResult(Ended: false);
        }

        if (token.SessionId is { } sessionId)
        {
            await _credentialRevocation.TerminateSessionAsync(sessionId, token.UserId, Reason, cancellationToken);
        }
        else if (!token.IsRevoked)
        {
            // A token issued outside any session row: revoke the token itself.
            token.Revoke(token.UserId, "User initiated logout");
            await _refreshTokenRepository.UpdateAsync(token, cancellationToken);
        }

        await _credentialRevocation.RevokeIdpSessionAsync(request.IdpSessionToken, cancellationToken);

        await _publisher.Publish(new UserLoggedOutEvent(token.UserId, false), cancellationToken);

        _logger.LogInformation("User {UserId} signed out with the first-party refresh cookie", token.UserId);
        return new LogoutWithRefreshCookieResult(Ended: true);
    }
}
