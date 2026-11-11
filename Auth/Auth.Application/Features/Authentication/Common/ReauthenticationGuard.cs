using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
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
    private readonly ICurrentAccessToken _accessToken;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReauthenticationGuard> _logger;

    public ReauthenticationGuard(
        IUserSessionRepository sessionRepository,
        IOptionsMonitor<TwoFactorSettings> settings,
        ICurrentAccessToken accessToken,
        TimeProvider timeProvider,
        ILogger<ReauthenticationGuard> logger)
    {
        _sessionRepository = sessionRepository;
        _settings = settings;
        _accessToken = accessToken;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<UserSession>> EnsureLiveSessionAsync(
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

        return session;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<RecentSession>> EnsureRecentSignInAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var live = await EnsureLiveSessionAsync(userId, sessionId, cancellationToken);
        if (live.IsError)
        {
            return live.Errors;
        }

        var session = live.Value;

        // Read per request, so the window can be changed without a restart, and
        // brought inside its range: no value — a typo in a file included — turns the
        // check off.
        var maxAgeMinutes = Math.Clamp(
            _settings.CurrentValue.ReauthenticationMaxAgeMinutes,
            TwoFactorSettings.MinReauthenticationMaxAgeMinutes,
            TwoFactorSettings.MaxReauthenticationMaxAgeMinutes);

        var oldestAccepted = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-maxAgeMinutes);
        if (session.CreatedAt >= oldestAccepted)
        {
            // Dated at the sign-in, which is when what the row first recorded was proved (D-B2).
            return new RecentSession(session.Id, session.DeviceName, session.Methods, session.CreatedAt);
        }

        // Past its sign-in, what counts is what the last proof inside the session proved, never
        // what the session ever proved: two factors an hour ago and a password just now are a
        // password just now. Read apart, and only here: see GetLastProofAsync.
        var proof = await _sessionRepository.GetLastProofAsync(session.Id, cancellationToken);
        if (proof is not null && proof.ProvedAtUtc >= oldestAccepted)
        {
            // And only for the tokens issued after it (REVIEW-FIXES 152 F23). The sign-out the
            // proof replaced ended every copy of the session's token; a proof given in one browser
            // must not serve a copy held elsewhere. The window refreshes the token of the browser
            // that proved, before it hands back, and that token passes.
            return IssuedSince(proof.ProvedAtUtc)
                ? new RecentSession(session.Id, session.DeviceName, proof.Methods, proof.ProvedAtUtc)
                : Refuse(userId, "the access token was issued before the last proof");
        }

        return Refuse(userId, $"the sign-in, and any proof since, is older than {maxAgeMinutes} minutes");
    }

    /// <summary>
    /// Whether the request's access token was issued at or after <paramref name="provedAtUtc"/>.
    /// Its <c>iat</c> holds whole seconds, so the proof is read to its own second: a token minted
    /// in that second passes, which only a holder of the session's refresh token can do. A token
    /// that names no time fails closed. Both times are the API's clock.
    /// </summary>
    private bool IssuedSince(DateTime provedAtUtc) =>
        _accessToken.IssuedAtUtc is { } issuedAt
        && issuedAt >= provedAtUtc.AddTicks(-(provedAtUtc.Ticks % TimeSpan.TicksPerSecond));

    /// <inheritdoc />
    public async Task<ErrorOr<RecentSession>> EnsureRecentTwoFactorSignInAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var session = await EnsureRecentSignInAsync(userId, sessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // A step-up inside the session counts as much as a code at sign-in: either
        // way the session row holds both methods.
        return session.Value.Methods.IsMfaSatisfied
            ? session
            : Refuse(userId, "the session has not proved a second factor");
    }

    private Error Refuse(Guid userId, string reason)
    {
        _logger.LogInformation(
            "A change to two-factor authentication for user {UserId} asked for a fresh sign-in: {Reason}",
            userId, reason);

        return AuthErrors.ReauthenticationRequired;
    }
}
