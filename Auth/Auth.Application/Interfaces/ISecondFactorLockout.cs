namespace Auth.Application.Interfaces;

/// <summary>
/// The second factor's consecutive-failure limit, and what reaching it leaves
/// behind: one log line (<see cref="Domain.Constants.AccountLockSignalCodes.TwoFactorHardLocked"/>)
/// and <see cref="Domain.Events.UserHardLockedEvent"/> for the second factor.
/// </summary>
/// <remarks>
/// The limit is the password's (<c>Password:ConsecutiveFailureLimit</c>, normalized
/// by <see cref="Configuration.PasswordSettingsNormalizer"/>): NIST SP 800-63B-4
/// §3.2.2 counts each authenticator's consecutive failures against the same ceiling.
/// Every reader of the limit asks here — the reservation, the verifier's answers,
/// and the administrator's reset gate — so they can never disagree.
/// </remarks>
public interface ISecondFactorLockout
{
    /// <summary>
    /// Gets the count at which the second factor stops accepting codes (Hard′), or
    /// null under the Legacy mode, which caps nothing.
    /// </summary>
    int? HardLimit { get; }

    /// <summary>
    /// Reports a reservation the store granted, with the count it left. The one
    /// whose count equals <see cref="HardLimit"/> is the transition — the statement
    /// is atomic, so there is exactly one per episode — and only it writes a line
    /// and publishes the event, after the write and whatever the caller does next.
    /// </summary>
    /// <param name="userId">The account whose factor was counted.</param>
    /// <param name="failedAttempts">The count the reservation left.</param>
    Task ReportReservationAsync(Guid userId, int failedAttempts);
}
