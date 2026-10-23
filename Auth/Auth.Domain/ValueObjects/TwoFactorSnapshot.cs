using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// A user's two-factor row as one read saw it, with the TOTP secret still
/// encrypted. It answers only what a read may answer — whether the factor is on,
/// whether it looked locked — and never stands in for the conditional writes that
/// decide when requests race.
/// </summary>
public sealed record class TwoFactorSnapshot
{
    public TwoFactorSnapshot(
        Guid userId,
        string? protectedSecretKey,
        string? recoveryCodes,
        bool isEnabled,
        int failedAttempts,
        DateTime? lockedUntil,
        string? pendingSecretKey = null,
        DateTime? pendingSecretCreatedAt = null,
        DateTime? modifiedAt = null,
        DateTime? emailCodeEnabledAt = null,
        int emailFailedAttempts = 0,
        DateTime? emailLockedUntil = null,
        bool recoveryCodesQualified = true)
    {
        UserId = userId;
        ProtectedSecretKey = protectedSecretKey;
        RecoveryCodes = recoveryCodes;
        IsEnabled = isEnabled;
        FailedAttempts = failedAttempts;
        LockedUntil = lockedUntil;
        PendingSecretKey = pendingSecretKey;
        PendingSecretCreatedAt = pendingSecretCreatedAt;
        ModifiedAt = modifiedAt;
        EmailCodeEnabledAt = emailCodeEnabledAt;
        EmailFailedAttempts = emailFailedAttempts;
        EmailLockedUntil = emailLockedUntil;
        RecoveryCodesQualified = recoveryCodesQualified;
    }

    /// <summary>
    /// Gets the ID of the user the row belongs to.
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Gets the TOTP secret exactly as stored: encrypted. The read never decrypts
    /// it, so a later write can compare against the stored text itself. Null on a
    /// row whose factor is the emailed code, which has no authenticator app.
    /// </summary>
    public string? ProtectedSecretKey { get; }

    /// <summary>
    /// Gets the JSON array of recovery-code hashes, byte for byte as stored.
    /// </summary>
    public string? RecoveryCodes { get; }

    /// <summary>
    /// Gets whether two-factor authentication was enabled when read.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the failed-verification count when read.
    /// </summary>
    public int FailedAttempts { get; }

    /// <summary>
    /// Gets the UTC time until which the factor was locked when read.
    /// </summary>
    public DateTime? LockedUntil { get; }

    /// <summary>
    /// Gets the replacement secret of an enabled factor, exactly as stored
    /// (encrypted), while it waits for its confirming code; null when no
    /// replacement was started.
    /// </summary>
    public string? PendingSecretKey { get; }

    /// <summary>
    /// Gets the UTC time the replacement secret was issued.
    /// </summary>
    public DateTime? PendingSecretCreatedAt { get; }

    /// <summary>
    /// Gets the UTC time of the row's last change, by the database's clock. Every
    /// counted failure stamps it, and nothing that a code must pass through does
    /// once the factor is hard-locked (the reservation no longer matches), so on a
    /// hard-locked factor it is the time of the failure that reached the limit.
    /// Setup refuses a pending factor at the limit, so it does not move the stamp
    /// either.
    /// </summary>
    public DateTime? ModifiedAt { get; }

    /// <summary>
    /// Gets when the emailed code became the account's second factor; null when it
    /// is not. Never set together with <see cref="IsEnabled"/>
    /// (<c>CK_TwoFactorAuth_SingleFactor</c>).
    /// </summary>
    public DateTime? EmailCodeEnabledAt { get; }

    /// <summary>
    /// Gets the consecutive wrong emailed codes when read, counted apart from
    /// <see cref="FailedAttempts"/>: a wrong emailed code never locks the
    /// authenticator app or the recovery codes, nor the other way round.
    /// </summary>
    public int EmailFailedAttempts { get; }

    /// <summary>
    /// Gets the UTC time until which the emailed code was locked when read.
    /// </summary>
    public DateTime? EmailLockedUntil { get; }

    /// <summary>
    /// Gets whether the stored recovery codes count as a second factor: issued with
    /// the authenticator app or in a session that proved two factors. Codes issued
    /// when the emailed code was switched on, after the password alone, do not (FA3).
    /// </summary>
    public bool RecoveryCodesQualified { get; }

    /// <summary>
    /// Gets whether the emailed code is the account's second factor.
    /// </summary>
    public bool HasEmailCode => EmailCodeEnabledAt.HasValue;

    /// <summary>
    /// Gets whether the account has a second factor of either kind: the
    /// authenticator app, or the emailed code. What a sign-in challenge needs, and
    /// what lets a recovery code be presented at all. Never a two-factor policy's
    /// answer: the emailed code satisfies none.
    /// </summary>
    public bool HasSecondFactor => IsEnabled || HasEmailCode;

    /// <summary>
    /// Gets whether the emailed code was locked for a while when read. A fast path
    /// only, like <see cref="IsLocked"/>.
    /// </summary>
    public bool IsEmailCodeLocked => EmailLockedUntil.HasValue && EmailLockedUntil.Value > DateTime.UtcNow;

    /// <summary>
    /// Whether the emailed code had reached the consecutive-failure limit when read.
    /// Its own count: it locks the emailed code alone, never the recovery codes.
    /// </summary>
    /// <param name="hardLimit">The limit in force (Hard′), or null when none is.</param>
    public bool IsEmailCodeHardLocked(int? hardLimit) => hardLimit is { } limit && EmailFailedAttempts >= limit;

    /// <summary>
    /// Gets whether the factor was locked when read. A fast path only: the attempt
    /// reservation checks the lock again in the same statement that counts.
    /// </summary>
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;

    /// <summary>
    /// Whether the factor had reached the consecutive-failure limit when read: the
    /// second-factor hard lock. It then accepts no code — from the authenticator app
    /// or a recovery code — until it is reset. No limit (null, the Legacy mode)
    /// means no hard lock. The one definition of it, which the reservation states
    /// as its own condition. The emailed code's count is its own
    /// (<see cref="IsEmailCodeHardLocked"/>): it never locks the recovery codes.
    /// </summary>
    /// <param name="hardLimit">The limit in force (Hard′), or null when none is.</param>
    public bool IsHardLocked(int? hardLimit) => hardLimit is { } limit && FailedAttempts >= limit;

    /// <summary>
    /// Whether an administrator may remove this factor now (LK4 (4)). Below the
    /// limit, yes — on both counts, the authenticator app's and recovery codes', and
    /// the emailed code's (S24): whoever drove either to the limit passed the
    /// password. A hard-locked factor only once the account's owner changed the
    /// password AFTER the failure that locked it: whoever drove the factor to the
    /// limit passed the first sign-in step, so removing the factor first would hand
    /// them the account. A reset by email is the expected way; a change from a
    /// signed-in session counts too, though such a session need not have proved the
    /// factor (one opened before it was set up never did), so the gate shows that the
    /// password moved, not who moved it: the administrator confirms the person
    /// outside email. A sign-in that only re-hashes the password does not move the
    /// stamp. A password change and a counted failure are both stamped by the
    /// database's clock; the stamp written when the account was created comes from
    /// the application's, and could open the gate only if that clock ran more than a
    /// day ahead (reaching the limit takes about a day of timed locks after the
    /// factor was set up). An account with no recorded password change, or a row
    /// with no change stamp, is refused.
    /// </summary>
    /// <param name="hardLimit">The limit in force (Hard′), or null when none is.</param>
    /// <param name="passwordChangedAt">When the account's password last changed.</param>
    /// <returns>Success, or <c>TwoFactor.PasswordResetRequiredFirst</c>.</returns>
    public ErrorOr<Success> EnsureAdministratorMayReset(int? hardLimit, DateTime? passwordChangedAt) =>
        (!IsHardLocked(hardLimit) && !IsEmailCodeHardLocked(hardLimit))
        || (passwordChangedAt is { } changedAt && ModifiedAt is { } lockedAt && changedAt > lockedAt)
            ? Result.Success
            : TwoFactorErrors.PasswordResetRequiredFirst;

    /// <summary>
    /// Whether an enabled factor held a replacement secret young enough to be
    /// confirmed, when read: issued less than
    /// <see cref="TwoFactorAuth.PendingReplacementLifetimeMinutes"/> before
    /// <paramref name="utcNow"/>. A fast path only, like <see cref="IsLocked"/>: the
    /// confirming write checks the age again, against the database's own clock.
    /// </summary>
    public bool HasPendingReplacement(DateTime utcNow) =>
        IsEnabled
        && !string.IsNullOrEmpty(PendingSecretKey)
        && PendingSecretCreatedAt is { } issuedAt
        && issuedAt > utcNow.AddMinutes(-TwoFactorAuth.PendingReplacementLifetimeMinutes);

    /// <summary>
    /// The same row with the replacement secret in place of the current one: what
    /// a code from the NEW authenticator is checked against, by the same check
    /// every other authenticator code goes through.
    /// </summary>
    /// <exception cref="InvalidOperationException">No replacement secret was read.</exception>
    public TwoFactorSnapshot AsPendingReplacement() =>
        new(
            UserId,
            string.IsNullOrEmpty(PendingSecretKey)
                ? throw new InvalidOperationException("The row holds no replacement secret.")
                : PendingSecretKey,
            RecoveryCodes,
            IsEnabled,
            FailedAttempts,
            LockedUntil,
            PendingSecretKey,
            PendingSecretCreatedAt,
            ModifiedAt,
            EmailCodeEnabledAt,
            EmailFailedAttempts,
            EmailLockedUntil,
            RecoveryCodesQualified);

    // The secrets' ciphertext and the recovery-code hashes stay out of any log
    // line or assertion message the snapshot reaches.
    public override string ToString() =>
        $"TwoFactorSnapshot {{ UserId = {UserId}, IsEnabled = {IsEnabled}, HasEmailCode = {HasEmailCode} }}";
}
