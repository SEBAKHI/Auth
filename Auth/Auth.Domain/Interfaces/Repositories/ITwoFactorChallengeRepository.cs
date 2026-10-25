using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for two-factor challenge operations.
/// </summary>
/// <remarks>
/// One table serves several purposes (<see cref="TwoFactorChallengePurpose"/>), and
/// every member names the one it serves: the members without a purpose parameter
/// serve a sign-in, which is what the anonymous endpoints present.
/// </remarks>
public interface ITwoFactorChallengeRepository
{
    /// <summary>
    /// Gets a sign-in challenge by any fingerprint the presented token may be stored
    /// under. A challenge of any other purpose is never returned.
    /// </summary>
    /// <param name="candidates">Every fingerprint the challenge token may be stored under.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The challenge if found, null otherwise.</returns>
    Task<TwoFactorChallenge?> GetByTokenHashAsync(CredentialHashCandidates candidates, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the newest live challenge of <paramref name="purpose"/> for a user: not
    /// used, not expired, and with an attempt left — an exhausted one can never be
    /// answered, so the caller opens a new one. The purposes answered by the account
    /// rather than by a token (the recovery of an account waiting for deletion) find
    /// theirs here.
    /// </summary>
    /// <returns>The challenge, or null when the user has no live one of that purpose.</returns>
    Task<TwoFactorChallenge?> GetLiveForUserAsync(
        Guid userId,
        TwoFactorChallengePurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new two-factor challenge.
    /// </summary>
    /// <param name="challenge">The challenge to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one verification attempt on a sign-in challenge before its code is
    /// checked. The cap, the expiry and single use are conditions of the same
    /// statement that counts, so of any number of concurrent requests at most
    /// <paramref name="maxAttempts"/> are ever let through to a check.
    /// </summary>
    /// <param name="challengeId">The challenge ID.</param>
    /// <param name="maxAttempts">The attempt allowance of a challenge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The attempt count including this reservation, or null when the challenge
    /// is used, expired or out of attempts — in which case no code may be checked.
    /// </returns>
    Task<int?> TryReserveAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// The reservation of <see cref="TryReserveAttemptAsync(Guid, int, CancellationToken)"/>
    /// on a challenge of <paramref name="purpose"/>: a row of another purpose matches nothing.
    /// </summary>
    Task<int?> TryReserveAttemptAsync(
        Guid challengeId,
        TwoFactorChallengePurpose purpose,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserves the sending of one emailed code for a challenge and stores its hash,
    /// in ONE statement whose conditions are every limit on sending (S24, EO6): the
    /// challenge is live, of <see cref="EmailCodeSendReservation.Purpose"/>, and has
    /// sent fewer than <see cref="TwoFactorChallenge.MaxEmailCodeSends"/> codes; the
    /// account's emailed code is bound and not locked (unless the purpose binds it);
    /// and the account's codes, every purpose together, stay under the per-window and
    /// the daily cap. The new hash becomes the live one, the earlier ones stay as
    /// superseded, and the challenge expires the lifetime after this send.
    /// </summary>
    /// <remarks>
    /// The account's sums are read under <c>UPDLOCK, HOLDLOCK</c> in the same
    /// statement, so of a burst of requests for one account exactly as many as the
    /// caps allow are let through, however many arrive at once.
    /// </remarks>
    /// <returns>
    /// The challenge's send count including this one and its new expiry, or null when
    /// a condition refused it and nothing was written.
    /// </returns>
    Task<EmailCodeSendReceipt?> TryReserveEmailCodeSendAsync(
        EmailCodeSendReservation reservation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused sign-in challenges for a user. A challenge of any
    /// other purpose stays: a new sign-in does not end a recovery or an enrollment.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> rows that fell out of use
    /// before <paramref name="olderThanUtc"/>, and reports how many went.
    /// </summary>
    /// <remarks>
    /// One bounded batch per call, deliberately. The caller loops, which keeps
    /// the row count of a single statement below the ~5000 locks at which SQL
    /// Server escalates to a table lock, and keeps each batch in its own implicit
    /// transaction so the log can be truncated between them. An unbounded DELETE
    /// over a table that accumulated for months would hold both.
    /// <para>
    /// The cutoff is passed in rather than computed here so every table is swept
    /// against one clock — the application's — instead of each statement reading
    /// the database server's.
    /// </para>
    /// <para>
    /// Every purpose is swept alike: each row expires by the same column.
    /// </para>
    /// </remarks>
    /// <returns>Rows deleted; a value below <paramref name="batchSize"/> means the table is drained.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// One request to email a code for a challenge, with every limit it must pass.
/// </summary>
/// <param name="ChallengeId">The challenge the code is emailed for.</param>
/// <param name="Purpose">The purpose the challenge must have.</param>
/// <param name="CodeHash">The keyed hash of the new code: never the code.</param>
/// <param name="LifetimeMinutes">How long the code is accepted after this send.</param>
/// <param name="RequireBoundFactor">
/// True unless the code binds the emailed code: then the account's emailed code
/// must be bound and not locked.
/// </param>
/// <param name="HardLimit">The consecutive-failure limit (Hard′) the factor must be under; null for none.</param>
/// <param name="WindowSeconds">The per-account window (<c>Email:RateLimitWindowSeconds</c>).</param>
/// <param name="WindowMax">The codes the account may be sent within the window (<c>Email:MaxOtpRequestsPerWindow</c>).</param>
/// <param name="DailyMax">The codes the account may be sent within 24 hours.</param>
public sealed record EmailCodeSendReservation(
    Guid ChallengeId,
    TwoFactorChallengePurpose Purpose,
    string CodeHash,
    int LifetimeMinutes,
    bool RequireBoundFactor,
    int? HardLimit,
    int WindowSeconds,
    int WindowMax,
    int DailyMax)
{
    // The hash of a live code stays out of every log line and assertion message.
    public override string ToString() =>
        $"EmailCodeSendReservation {{ ChallengeId = {ChallengeId}, Purpose = {Purpose} }}";
}

/// <summary>
/// What a reserved send wrote.
/// </summary>
/// <param name="SendCount">The challenge's codes, this one included.</param>
/// <param name="ExpiresAt">When the challenge, and so this code, stops being accepted (UTC).</param>
public sealed record EmailCodeSendReceipt(int SendCount, DateTime ExpiresAt);
