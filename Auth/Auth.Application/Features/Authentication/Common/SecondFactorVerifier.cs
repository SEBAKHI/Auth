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

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        SecondFactorPresentation presentation,
        CancellationToken cancellationToken) =>
        ReserveAsync(presentation, _lockout.HardLimit, cancellationToken);

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorReservation>> ReserveUnderTheTimedLockOnlyAsync(
        SecondFactorPresentation presentation,
        CancellationToken cancellationToken) =>
        ReserveAsync(presentation, hardLimit: null, cancellationToken);

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

        return await ReserveOnAsync(
            SecondFactorCounter.Shared, snapshot, challenge: null, hardLimit, cancellationToken);
    }

    /// <summary>
    /// The reservation for a code presented as one named factor: refused, uncounted,
    /// when the account does not hold that factor or the strategy's screen refuses
    /// it; otherwise counted on that factor's own counter.
    /// </summary>
    private async Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        SecondFactorPresentation presentation,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        var strategy = Strategy(presentation.Method);
        var snapshot = await _stateStore.GetSnapshotAsync(presentation.UserId, cancellationToken);

        // No second factor of any kind: the answer a missing factor has always had.
        if (snapshot is null || !snapshot.HasSecondFactor)
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        // A factor the account does not hold — an authenticator code for an account
        // whose factor is the emailed code, say — cannot be right. Nothing is counted:
        // there is nothing to guess.
        if (!strategy.IsBound(snapshot))
        {
            return UserErrors.InvalidTwoFactorCode;
        }

        var screened = strategy.Screen(presentation);
        if (screened.IsError)
        {
            return screened.Errors;
        }

        return await ReserveOnAsync(strategy.Counter, snapshot, presentation.Challenge, hardLimit, cancellationToken);
    }

    private async Task<ErrorOr<SecondFactorReservation>> ReserveOnAsync(
        SecondFactorCounter counter,
        TwoFactorSnapshot snapshot,
        Domain.Entities.TwoFactorChallenge? challenge,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        // A factor that already looks locked costs nothing: no attempt is
        // reserved on it, here or on any challenge the caller holds. The hard lock
        // first, and it is never answered as the timed one: waiting does not end
        // it, so the answer names the way out instead.
        if (counter.IsHardLocked(snapshot, hardLimit))
        {
            return counter.HardLockedError;
        }

        if (counter.IsLocked(snapshot))
        {
            return TwoFactorErrors.LockedOut;
        }

        // The read above cannot decide for a burst of requests that all made it
        // together. The reservation can: it counts and re-checks both locks in one
        // statement, and refuses every request past either limit.
        var failedAttempts = await counter.TryReserveAsync(_stateStore, snapshot.UserId, hardLimit, cancellationToken);
        if (failedAttempts is not { } counted)
        {
            return await RefusalAsync(counter, snapshot.UserId, hardLimit, cancellationToken);
        }

        // A transition only where the limit applies: past it on the exempted path
        // the factor is not hard-locked for that path, and nothing is reported.
        if (hardLimit is not null)
        {
            await _lockout.ReportReservationAsync(snapshot.UserId, counted);
        }

        return new SecondFactorReservation(snapshot, challenge);
    }

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        SecondFactorMethod method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return Strategy(method).VerifyAsync(reservation, code, cancellationToken);
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
            .VerifyAsync(reservation.With(reservation.Snapshot.AsPendingReplacement()), code, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorProof>> VerifyUncountedAuthenticatorAsync(
        TwoFactorSnapshot snapshot,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Only the emailed code's row: everywhere else an authenticator code is counted.
        if (!snapshot.HasEmailCode)
        {
            throw new InvalidOperationException("An uncounted authenticator check is only for an emailed-code row.");
        }

        return Strategy(SecondFactorMethod.Totp).VerifyAsync(new SecondFactorReservation(snapshot), code, cancellationToken);
    }

    /// <summary>
    /// Names a refused reservation by the row as it is now, nothing held: a
    /// concurrent request reached the hard limit first, or locked the factor for a
    /// while. A row that is gone gets the answer a refused reservation always had.
    /// </summary>
    private async Task<Error> RefusalAsync(
        SecondFactorCounter counter,
        Guid userId,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        var now = await _stateStore.GetSnapshotAsync(userId, cancellationToken);

        return now is not null && counter.IsHardLocked(now, hardLimit)
            ? counter.HardLockedError
            : TwoFactorErrors.LockedOut;
    }

    private ISecondFactorProofStrategy Strategy(SecondFactorMethod method) =>
        _strategies.TryGetValue(method, out var strategy)
            ? strategy
            : throw new InvalidOperationException($"No second-factor proof strategy is registered for {method}.");
}
