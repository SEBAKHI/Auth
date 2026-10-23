using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// One account-level counter of wrong second-factor codes, with the two locks it
/// raises: five failures lock it for a while, the consecutive-failure limit (Hard′)
/// for good. A factor presents its codes against exactly one counter.
/// </summary>
/// <remarks>
/// Two counters, and only two. The authenticator app and the recovery codes share
/// <c>TwoFactorAuth.FailedAttempts</c>, as they always have; the emailed code counts
/// in <c>TwoFactorAuth.EmailFailedAttempts</c> (S24, D-T33 B): NIST SP 800-63B §3.2.2
/// limits failures per authenticator, so wrong emailed codes never lock the recovery
/// codes the person is left with, nor the other way round. Both under the same Hard′.
/// </remarks>
public abstract class SecondFactorCounter
{
    private SecondFactorCounter()
    {
    }

    /// <summary>The authenticator app's and the recovery codes' counter.</summary>
    public static SecondFactorCounter Shared { get; } = new SharedCounter();

    /// <summary>The emailed code's counter.</summary>
    public static SecondFactorCounter EmailCode { get; } = new EmailCodeCounter();

    /// <summary>The answer for a code refused by the hard lock: never the timed one's.</summary>
    public abstract Error HardLockedError { get; }

    /// <summary>Whether the counter looked locked for a while when read. A fast path only.</summary>
    public abstract bool IsLocked(TwoFactorSnapshot snapshot);

    /// <summary>Whether the counter had reached <paramref name="hardLimit"/> when read. A fast path only.</summary>
    public abstract bool IsHardLocked(TwoFactorSnapshot snapshot, int? hardLimit);

    /// <summary>
    /// Counts one failure before any code is checked, in one statement that re-checks
    /// both locks.
    /// </summary>
    /// <returns>The count including this reservation, or null when nothing may be checked.</returns>
    public abstract Task<int?> TryReserveAsync(
        ITwoFactorStateStore stateStore,
        Guid userId,
        int? hardLimit,
        CancellationToken cancellationToken);

    private sealed class SharedCounter : SecondFactorCounter
    {
        public override Error HardLockedError => TwoFactorErrors.LockedAdminResetRequired;

        public override bool IsLocked(TwoFactorSnapshot snapshot) => snapshot.IsLocked;

        public override bool IsHardLocked(TwoFactorSnapshot snapshot, int? hardLimit) =>
            snapshot.IsHardLocked(hardLimit);

        public override Task<int?> TryReserveAsync(
            ITwoFactorStateStore stateStore,
            Guid userId,
            int? hardLimit,
            CancellationToken cancellationToken) =>
            stateStore.TryReserveAttemptAsync(userId, hardLimit, cancellationToken);
    }

    private sealed class EmailCodeCounter : SecondFactorCounter
    {
        // The way out is a completed password reset, which clears this count; the
        // recovery codes keep working meanwhile.
        public override Error HardLockedError => TwoFactorErrors.EmailCodeLocked;

        public override bool IsLocked(TwoFactorSnapshot snapshot) => snapshot.IsEmailCodeLocked;

        public override bool IsHardLocked(TwoFactorSnapshot snapshot, int? hardLimit) =>
            snapshot.IsEmailCodeHardLocked(hardLimit);

        public override Task<int?> TryReserveAsync(
            ITwoFactorStateStore stateStore,
            Guid userId,
            int? hardLimit,
            CancellationToken cancellationToken) =>
            stateStore.TryReserveEmailCodeAttemptAsync(userId, hardLimit, cancellationToken);
    }
}
