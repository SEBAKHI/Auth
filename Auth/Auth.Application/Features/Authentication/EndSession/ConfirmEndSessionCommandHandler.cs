using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// Ends the single sign-on session once the user has said so.
/// </summary>
/// <remarks>
/// Ends the SSO session through <see cref="ICredentialRevocationService.RevokeIdpSessionAsync"/>,
/// the same end as a sign-out in the console or the accounts app. With single logout on
/// (OI-155), every sign-in born from that session ends with it: each application's and
/// platform app's session whose refresh chain was linked to it. This reverses an earlier
/// choice, which left an application's tokens for it to discard: the relying party that sent
/// the browser here cannot discard the OTHER applications' sessions, and since OI-154 each
/// refresh token records the IdP session it was born from, so the provider can tell which
/// sessions this browser's sign-out means. An application that validates its access tokens
/// locally learns at its next refresh, which is refused.
/// <para>
/// Authenticated by the cookie alone: this request carries no bearer token,
/// because the browser making it was steered here by another site. The cookie is
/// SameSite=Lax, which is what stops a cross-site page from forging this call —
/// Lax withholds the cookie from cross-site POSTs, so a forged one arrives with
/// nothing to act on.
/// </para>
/// </remarks>
public class ConfirmEndSessionCommandHandler : IRequestHandler<ConfirmEndSessionCommand, ErrorOr<Success>>
{
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILogger<ConfirmEndSessionCommandHandler> _logger;

    public ConfirmEndSessionCommandHandler(
        ICredentialRevocationService credentialRevocation,
        ILogger<ConfirmEndSessionCommandHandler> logger)
    {
        _credentialRevocation = credentialRevocation;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ConfirmEndSessionCommand request, CancellationToken cancellationToken)
    {
        // Succeeds when there is nothing to do. Ending a session that has already
        // ended is the outcome the caller wanted, and answering an error would
        // turn a double click into a failure the user has to interpret.
        if (string.IsNullOrWhiteSpace(request.IdpSessionToken))
        {
            return Result.Success;
        }

        await _credentialRevocation.RevokeIdpSessionAsync(request.IdpSessionToken, cancellationToken);

        // The request, not the outcome: the cookie may name a session that has already ended.
        _logger.LogInformation("End of the single sign-on session confirmed by the browser");

        return Result.Success;
    }
}
