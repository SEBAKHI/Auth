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

        return await _credentialRevocation.TerminateSessionsAsync(
            request.UserId,
            request.ExceptSessionId,
            revokedBy: request.UserId,
            reason,
            cancellationToken);
    }
}
