using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Reads the IdP session cookie for every caller: the authorize endpoint, the
/// organization step, end-session, the refresh cookie's lifetime, and a
/// first-party refresh (OI-154).
/// Before this, each kept its own copy of "hash the cookie, load the row, check
/// it", and none knew about inactivity.
/// </summary>
/// <remarks>
/// The rule lives on <see cref="IdpSession.IsValidAt"/>; this class supplies the
/// clock and the idle limit, read per request so a change applies at once. A
/// session found idle is revoked here, the only place that sees it die, so raising
/// the limit or switching it off later does not bring THAT one back. A session
/// nobody read since it went idle is still unrevoked: a console change of the
/// limit ends those first (<see cref="IIdpSessionIdleLimitChange"/>).
/// </remarks>
public sealed class IdpSessionReader : IIdpSessionReader
{
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdpSessionReader> _logger;

    public IdpSessionReader(
        IIdpSessionRepository idpSessionRepository,
        ICredentialHasher credentialHasher,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        TimeProvider timeProvider,
        ILogger<IdpSessionReader> logger)
    {
        _idpSessionRepository = idpSessionRepository;
        _credentialHasher = credentialHasher;
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
            _credentialHasher.Candidates(CredentialPurpose.IdpSession, idpSessionToken), cancellationToken);
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

        if (mode != IdpSessionReadMode.Inspect)
        {
            await EndIfIdleAsync(session, now, idleLimit, cancellationToken);
        }

        return null;
    }

    /// <inheritdoc />
    public Task<bool> RecordUseAsync(Guid idpSessionId, CancellationToken cancellationToken) =>
        _idpSessionRepository.RecordActivityAsync(
            idpSessionId, UtcNow(), _idpSettings.IdpBrowserSessionIdleLimit, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> IsLiveAsync(Guid idpSessionId, CancellationToken cancellationToken)
    {
        var session = await _idpSessionRepository.GetByIdAsync(idpSessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        var now = UtcNow();
        var idleLimit = _idpSettings.IdpBrowserSessionIdleLimit;
        if (session.IsValidAt(now, idleLimit))
        {
            return true;
        }

        await EndIfIdleAsync(session, now, idleLimit, cancellationToken);
        return false;
    }

    /// <summary>
    /// Revokes a session this read found idle. Best effort: the caller already has
    /// its answer (the session is dead), so a failed write is a warning, never an
    /// error to the person; the next read finds the session idle again.
    /// </summary>
    private async Task EndIfIdleAsync(
        IdpSession session, DateTime now, TimeSpan idleLimit, CancellationToken cancellationToken)
    {
        if (session.IsRevoked || !session.IsIdleAt(now, idleLimit))
        {
            return;
        }

        try
        {
            session.Revoke();
            await _idpSessionRepository.UpdateAsync(session, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "IdP session {IdpSessionId} of user {UserId} was found idle but could not be ended",
                session.Id, session.UserId);
            return;
        }

        _logger.LogInformation(
            "IdP session {IdpSessionId} of user {UserId} ended: unused for {IdleMinutes} minutes",
            session.Id, session.UserId, (int)idleLimit.TotalMinutes);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
