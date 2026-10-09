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

    public SecondFactorVerifier(
        ITwoFactorStateStore stateStore,
        IEnumerable<ISecondFactorProofStrategy> strategies)
    {
        _stateStore = stateStore;

        // Two strategies claiming one method is a registration mistake: refuse it
        // here rather than let whichever registered last decide silently.
        _strategies = strategies.ToDictionary(strategy => strategy.Method);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        Guid userId,
        bool expectEnabled,
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
        // reserved on it, here or on any challenge the caller holds.
        if (snapshot.IsLocked)
        {
            return TwoFactorErrors.LockedOut;
        }

        // The read above cannot decide for a burst of requests that all made it
        // together. The reservation can: it counts and re-checks the lock in one
        // statement, and refuses every request past the maximum.
        if (await _stateStore.TryReserveAttemptAsync(userId, cancellationToken) is null)
        {
            return TwoFactorErrors.LockedOut;
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

    private ISecondFactorProofStrategy Strategy(SecondFactorMethod method) =>
        _strategies.TryGetValue(method, out var strategy)
            ? strategy
            : throw new InvalidOperationException($"No second-factor proof strategy is registered for {method}.");
}
