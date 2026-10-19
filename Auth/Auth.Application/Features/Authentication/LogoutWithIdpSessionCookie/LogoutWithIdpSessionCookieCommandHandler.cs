using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.LogoutWithIdpSessionCookie;

/// <summary>
/// Ends the browser's SSO session, and with single logout on the sign-ins born from it, for the
/// person whose app session the sign-out names (<see cref="ICredentialRevocationService.RevokeIdpSessionOfSessionOwnerAsync"/>).
/// </summary>
public class LogoutWithIdpSessionCookieCommandHandler
    : IRequestHandler<LogoutWithIdpSessionCookieCommand, ErrorOr<LogoutWithIdpSessionCookieResult>>
{
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILogger<LogoutWithIdpSessionCookieCommandHandler> _logger;

    public LogoutWithIdpSessionCookieCommandHandler(
        ICredentialRevocationService credentialRevocation,
        ILogger<LogoutWithIdpSessionCookieCommandHandler> logger)
    {
        _credentialRevocation = credentialRevocation;
        _logger = logger;
    }

    public async Task<ErrorOr<LogoutWithIdpSessionCookieResult>> Handle(
        LogoutWithIdpSessionCookieCommand request,
        CancellationToken cancellationToken)
    {
        var ended = await _credentialRevocation.RevokeIdpSessionOfSessionOwnerAsync(
            request.IdpSessionToken, request.SessionId);

        if (ended)
        {
            _logger.LogInformation(
                "Session {SessionId} signed out with the SSO cookie alone; its SSO session is ended", request.SessionId);
        }

        return new LogoutWithIdpSessionCookieResult(ended);
    }
}
