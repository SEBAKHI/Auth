using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Reads the IdP session cookie for every caller: the authorize endpoint, the
/// organization step, end-session, and the refresh cookie's lifetime (OI-154).
/// Before this, each kept its own copy of "hash the cookie, load the row, check
/// it", and none knew about inactivity.
/// </summary>
/// <remarks>
/// The rule lives on <see cref="IdpSession.IsValidAt"/>; this class supplies the
/// clock and the idle limit, read per request so a change applies at once. A
/// session found idle is revoked here, the only place that sees it die: without
/// that, raising the limit or switching it off would bring it back.
/// </remarks>
public sealed class IdpSessionReader : IIdpSessionReader
{
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdpSessionReader> _logger;

    public IdpSessionReader(
        IIdpSessionRepository idpSessionRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        TimeProvider timeProvider,
        ILogger<IdpSessionReader> logger)
    {
        _idpSessionRepository = idpSessionRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _idpSettings = idpSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdpSession?> FindLiveAsync(
        string? idpSessionToken, IdpSessionReadMode mode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idpSessionToken))
        {
            return null;
        }

        var session = await _idpSessionRepository.GetByTokenHashAsync(
            _refreshTokenKeyService.ComputeTokenHash(idpSessionToken), cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = UtcNow();
        var idleLimit = _idpSettings.IdpBrowserSessionIdleLimit;

        if (session.IsValidAt(now, idleLimit))
        {
            if (mode != IdpSessionReadMode.Use)
            {
                return session;
            }

            // The write re-checks every condition in one statement. Refused, the
            // session ended between the read and the write (a sign-out in another
            // tab): it is no longer the browser's session to use.
            if (!await _idpSessionRepository.RecordActivityAsync(session.Id, now, idleLimit, cancellationToken))
            {
                return null;
            }

            session.RecordActivity(now);
            return session;
        }

        if (mode != IdpSessionReadMode.Inspect && !session.IsRevoked && session.IsIdleAt(now, idleLimit))
        {
            session.Revoke();
            await _idpSessionRepository.UpdateAsync(session, cancellationToken);

            _logger.LogInformation(
                "IdP session {IdpSessionId} of user {UserId} ended: unused for {IdleMinutes} minutes",
                session.Id, session.UserId, (int)idleLimit.TotalMinutes);
        }

        return null;
    }

    /// <inheritdoc />
    public Task<bool> RecordUseAsync(Guid idpSessionId, CancellationToken cancellationToken) =>
        _idpSessionRepository.RecordActivityAsync(
            idpSessionId, UtcNow(), _idpSettings.IdpBrowserSessionIdleLimit, cancellationToken);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
