using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// Ends the single sign-on session once the user has said so.
/// </summary>
/// <remarks>
/// Ends the SSO session and nothing else, deliberately. What a relying party
/// holds — its own access and refresh tokens — is the relying party's to discard;
/// we cannot tell which tokens belong to which application from a browser
/// navigation, and the specification puts that duty on the client. What we own,
/// and what the client cannot reach, is the session that would otherwise sign
/// the user straight back in without a password.
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
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILogger<ConfirmEndSessionCommandHandler> _logger;

    public ConfirmEndSessionCommandHandler(
        IIdpSessionRepository idpSessionRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILogger<ConfirmEndSessionCommandHandler> logger)
    {
        _idpSessionRepository = idpSessionRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
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

        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(request.IdpSessionToken);
        var session = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (session is { IsRevoked: false })
        {
            session.Revoke();
            await _idpSessionRepository.UpdateAsync(session, cancellationToken);

            _logger.LogInformation(
                "Single sign-on session ended for user {UserId} by relying-party request", session.UserId);
        }

        return Result.Success;
    }
}
