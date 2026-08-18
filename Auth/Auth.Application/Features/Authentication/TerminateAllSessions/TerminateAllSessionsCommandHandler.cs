using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.TerminateAllSessions;

/// <summary>
/// Handler for the terminate all sessions command. Delegates to the shared
/// credential-revocation service (also used by the account deletion flows).
/// </summary>
public class TerminateAllSessionsCommandHandler : IRequestHandler<TerminateAllSessionsCommand, ErrorOr<int>>
{
    private readonly ICredentialRevocationService _credentialRevocation;

    public TerminateAllSessionsCommandHandler(ICredentialRevocationService credentialRevocation)
    {
        _credentialRevocation = credentialRevocation;
    }

    public async Task<ErrorOr<int>> Handle(
        TerminateAllSessionsCommand request,
        CancellationToken cancellationToken)
    {
        var reason = request.ExceptSessionId.HasValue
            ? "User terminated all other sessions"
            : "User terminated all sessions";

        // Revoke, not merely terminate: the SSO session lives in its own table
        // and outlives every UserSessions operation, so a "sign out everywhere"
        // that skipped it left the browser still able to mint authorization
        // codes for every entitled application — the button would report a
        // number and lock nothing out.
        return await _credentialRevocation.RevokeCredentialsAsync(
            request.UserId,
            request.ExceptSessionId,
            request.IdpSessionToken,
            revokedBy: request.UserId,
            reason,
            cancellationToken);
    }
}
