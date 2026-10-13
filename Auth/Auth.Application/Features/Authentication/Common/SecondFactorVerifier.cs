using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one place a second-factor code is checked: reserve first, verify second.
/// </summary>
public class SecondFactorVerifier : ISecondFactorVerifier
{
    private readonly ITwoFactorStateStore _stateStore;
    private readonly IReadOnlyDictionary<SecondFactorMethod, ISecondFactorProofStrategy> _strategies;
    private readonly ISecondFactorLockout _lockout;

    public SecondFactorVerifier(
        ITwoFactorStateStore stateStore,
        IEnumerable<ISecondFactorProofStrategy> strategies,
        ISecondFactorLockout lockout)
    {
        _stateStore = stateStore;
        _lockout = lockout;

        // Two strategies claiming one method is a registration mistake: refuse it
        // here rather than let whichever registered last decide silently.
        _strategies = strategies.ToDictionary(strategy => strategy.Method);
    }

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        Guid userId,
        bool expectEnabled,
        CancellationToken cancellationToken) =>
        ReserveAsync(userId, expectEnabled, _lockout.HardLimit, cancellationToken);

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorReservation>> ReserveUnderTheTimedLockOnlyAsync(
        Guid userId,
        bool expectEnabled,
        CancellationToken cancellationToken) =>
        ReserveAsync(userId, expectEnabled, hardLimit: null, cancellationToken);

    /// <summary>
    /// The one reservation, under <paramref name="hardLimit"/>: the limit in force,
    /// or null for the timed lock alone (Legacy, or the one path exempted).
    /// </summary>
    private async Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        Guid userId,
        bool expectEnabled,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        var snapshot = await _stateStore.GetSnapshotAsync(userId, cancellationToken);

        // The same answers the enable and disable paths have always given for a
        // factor in the wrong state.
        if (snapshot is null)
        {
            return expectEnabled ? UserErrors.TwoFactorNotEnabled : TwoFactorErrors.SetupRequired;
        }

        if (snapshot.IsEnabled != expectEnabled)
        {
            return expectEnabled ? UserErrors.TwoFactorNotEnabled : UserErrors.TwoFactorAlreadyEnabled;
        }

        // A factor that already looks locked costs nothing: no attempt is
        // reserved on it, here or on any challenge the caller holds. The hard lock
        // first, and it is never answered as the timed one: waiting does not end
        // it, so the answer names the way out instead.
        if (snapshot.IsHardLocked(hardLimit))
        {
            return TwoFactorErrors.LockedAdminResetRequired;
        }

        if (snapshot.IsLocked)
        {
            return TwoFactorErrors.LockedOut;
        }

        // The read above cannot decide for a burst of requests that all made it
        // together. The reservation can: it counts and re-checks both locks in one
        // statement, and refuses every request past either limit.
        var failedAttempts = await _stateStore.TryReserveAttemptAsync(userId, hardLimit, cancellationToken);
        if (failedAttempts is not { } counted)
        {
            return await RefusalAsync(userId, hardLimit, cancellationToken);
        }

        // A transition only where the limit applies: past it on the exempted path
        // the factor is not hard-locked for that path, and nothing is reported.
        if (hardLimit is not null)
        {
            await _lockout.ReportReservationAsync(userId, counted);
        }

        return new SecondFactorReservation(snapshot);
    }

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        SecondFactorMethod method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return Strategy(method).VerifyAsync(reservation.Snapshot, code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SecondFactorProof>> VerifyReplacementAsync(
        SecondFactorReservation reservation,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (string.IsNullOrEmpty(reservation.Snapshot.PendingSecretKey))
        {
            return TwoFactorErrors.NoPendingReplacement;
        }

        // The waiting secret in place of the current one: the authenticator check
        // itself does not change.
        return await Strategy(SecondFactorMethod.Totp)
            .VerifyAsync(reservation.Snapshot.AsPendingReplacement(), code, cancellationToken);
    }

    /// <summary>
    /// Names a refused reservation by the row as it is now, nothing held: a
    /// concurrent request reached the hard limit first, or locked the factor for a
    /// while. A row that is gone gets the answer a refused reservation always had.
    /// </summary>
    private async Task<Error> RefusalAsync(Guid userId, int? hardLimit, CancellationToken cancellationToken)
    {
        var now = await _stateStore.GetSnapshotAsync(userId, cancellationToken);

        return now?.IsHardLocked(hardLimit) == true
            ? TwoFactorErrors.LockedAdminResetRequired
            : TwoFactorErrors.LockedOut;
    }

    private ISecondFactorProofStrategy Strategy(SecondFactorMethod method) =>
        _strategies.TryGetValue(method, out var strategy)
            ? strategy
            : throw new InvalidOperationException($"No second-factor proof strategy is registered for {method}.");
}
