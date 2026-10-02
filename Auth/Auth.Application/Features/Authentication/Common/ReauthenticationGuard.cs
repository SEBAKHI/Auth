using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one check that a session signed in recently, run before any change to the
/// second factor reads a secret, reserves an attempt or writes anything.
/// </summary>
public class ReauthenticationGuard : IReauthenticationGuard
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IOptionsMonitor<TwoFactorSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReauthenticationGuard> _logger;

    public ReauthenticationGuard(
        IUserSessionRepository sessionRepository,
        IOptionsMonitor<TwoFactorSettings> settings,
        TimeProvider timeProvider,
        ILogger<ReauthenticationGuard> logger)
    {
        _sessionRepository = sessionRepository;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<RecentSession>> EnsureRecentSignInAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        // A token without a session id — issued before tokens carried one, or a
        // legacy jti in its place, which never names a session row — cannot show
        // when its sign-in happened, so it is asked to sign in again: fail closed.
        if (sessionId is not { } id)
        {
            return Refuse(userId, "the token names no session");
        }

        var session = await _sessionRepository.GetByIdAsync(id, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return Refuse(userId, "no session row for the token");
        }

        if (!session.IsActive)
        {
            return Refuse(userId, "the session has ended");
        }

        // Read per request, so the window can be changed without a restart, and
        // brought inside its range: no value — a typo in a file included — turns the
        // check off.
        var maxAgeMinutes = Math.Clamp(
            _settings.CurrentValue.ReauthenticationMaxAgeMinutes,
            TwoFactorSettings.MinReauthenticationMaxAgeMinutes,
            TwoFactorSettings.MaxReauthenticationMaxAgeMinutes);

        var oldestAccepted = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-maxAgeMinutes);
        if (session.CreatedAt < oldestAccepted)
        {
            return Refuse(userId, $"the sign-in is older than {maxAgeMinutes} minutes");
        }

        return new RecentSession(session.Id, session.DeviceName);
    }

    private Error Refuse(Guid userId, string reason)
    {
        _logger.LogInformation(
            "A change to two-factor authentication for user {UserId} asked for a fresh sign-in: {Reason}",
            userId, reason);

        return AuthErrors.ReauthenticationRequired;
    }
}
