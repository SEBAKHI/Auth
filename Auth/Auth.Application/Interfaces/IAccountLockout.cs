using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Interfaces;

/// <summary>
/// The automatic lock's writes, and what each one leaves behind: one log line per
/// transition (<see cref="Domain.Constants.AccountLockSignalCodes"/>) and, when the
/// hard limit is reached, <see cref="Domain.Events.UserHardLockedEvent"/>.
/// </summary>
/// <remarks>
/// Every sign-in, reset and change path goes through here rather than calling the
/// repository's lock statements itself, so the limits it applies are always the
/// normalized ones (<see cref="Configuration.PasswordSettingsNormalizer"/>) and no
/// transition goes unsignalled. Administrator actions are not here: they write
/// through <c>IUserRepository.SetAccountStatusAsync</c> and keep their own events.
/// </remarks>
public interface IAccountLockout
{
    /// <summary>
    /// Counts one wrong password. Every Max′-th consecutive failure locks for the
    /// lockout duration; reaching Hard′ locks with no expiry (not under Legacy).
    /// </summary>
    Task RecordPasswordFailureAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a lapsed lock to active before a sign-in is judged. The count is
    /// kept (under Legacy it starts again). Not an unlock, so no line is written.
    /// </summary>
    Task RepairLapsedLockAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Lifts an automatic lock and starts a new count after a completed password
    /// reset or change. An administrator's lock is not touched.
    /// </summary>
    Task ClearAutomaticLockAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a completed sign-in. Only one that proved the password lifts an
    /// automatic lock and starts a new count; any other writes the last-login
    /// fields alone.
    /// </summary>
    Task RecordSignInAsync(Guid userId, string? ipAddress, bool passwordProven, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the line for a familiar source let through to the password check
    /// while an automatic lock (<paramref name="kind"/>) is live. Admission lifts
    /// nothing.
    /// </summary>
    void ReportFamiliarAdmission(Guid userId, AccountLockKind kind, int failedAttempts);
}
