using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence for pending self-registrations. Every operation that decides
/// something about a code runs under the row's lock inside one transaction,
/// because the decisions — is the code live, has it taken its five guesses,
/// does it match — are only true while nothing else can change the row.
/// </summary>
public interface IPendingRegistrationRepository
{
    /// <summary>
    /// Starts, or re-enters, the registration for an address: one transaction
    /// that locks the address's unconsumed row (or the gap where it would be),
    /// decides whether a code is issued, and commits.
    /// </summary>
    /// <remarks>
    /// The rules, in order:
    /// a live code that reached the outbox is left alone (it is in someone's
    /// inbox); a code that never reached the outbox, or that is dead (expired
    /// or exhausted), is replaced in place if the address's mail window allows
    /// one more; an address with no unconsumed row gets a new row and a code.
    /// The plaintext code is returned only when one was issued, exists nowhere
    /// but in the caller's memory, and is what the caller mails.
    /// </remarks>
    Task<PendingRegistrationStart> StartAsync(PendingRegistrationStartRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a code against the live row behind <paramref name="handle"/>,
    /// under the row's lock: the attempt gate and the hash comparison are
    /// evaluated together, so a burst of concurrent guesses cannot each read
    /// the count before any of them raised it. A wrong code raises the count
    /// and commits; a right one stamps the verified time and commits; nothing
    /// is consumed. Used by the check-only step and by the completion step's
    /// first check alike — one increment site.
    /// </summary>
    Task<PendingRegistrationCodeCheck> CheckCodeUnderLockAsync(string handle, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the message carrying the code whose hash is
    /// <paramref name="otpHash"/> reached the outbox. Guarded on the hash so a
    /// rotation that happened in between is not stamped as mailed.
    /// </summary>
    Task MarkMailedAsync(Guid id, string otpHash, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the unconsumed row for an address as consumed — a Users row now
    /// exists for it, through whichever door. Returns how many rows changed
    /// (0 when there was nothing pending).
    /// </summary>
    Task<int> ConsumeByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> rows whose code expired
    /// before <paramref name="olderThanUtc"/>, consumed or not, and reports how
    /// many went. Hygiene, not security: expiry is enforced on every read.
    /// </summary>
    /// <returns>Rows deleted; a value below <paramref name="batchSize"/> means the table is drained.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IPendingRegistrationRepository.StartAsync"/> needs to decide and to issue.</summary>
/// <param name="Handle">The client-facing handle for the address.</param>
/// <param name="Email">The address as typed (trimmed; the row stores it lower-cased).</param>
/// <param name="PreferredLanguage">Language of this request, stored with a code it issues.</param>
/// <param name="ExpirationMinutes">Lifetime of a code issued by this call.</param>
/// <param name="MailWindow">The window the per-address code cap is counted in.</param>
/// <param name="MaxMailsPerWindow">Codes an address may be issued per window.</param>
public sealed record PendingRegistrationStartRequest(
    string Handle,
    string Email,
    string? PreferredLanguage,
    int ExpirationMinutes,
    TimeSpan MailWindow,
    int MaxMailsPerWindow);

/// <summary>What a start did.</summary>
public enum PendingRegistrationStartAction
{
    /// <summary>A code was issued — on a new row or in place of a dead one — and must be mailed.</summary>
    Minted,

    /// <summary>Nothing changed: a live code is already out, or the mail window is spent.</summary>
    Unchanged
}

/// <summary>The row after a start, what happened to it, and the code to mail when one was issued.</summary>
public sealed record PendingRegistrationStart(
    PendingRegistration Row,
    PendingRegistrationStartAction Action,
    string? Code);

/// <summary>How a presented code fared against the row's current one.</summary>
public enum PendingRegistrationCodeOutcome
{
    /// <summary>No unconsumed, unexpired row behind the handle.</summary>
    NotFound,

    /// <summary>The current code already took its five wrong guesses.</summary>
    Exhausted,

    /// <summary>Wrong code; one more attempt has been charged and committed.</summary>
    Wrong,

    /// <summary>The code matches; nothing was consumed.</summary>
    Match
}

/// <summary>The outcome of a locked code check, with the row when one was found.</summary>
public sealed record PendingRegistrationCodeCheck(
    PendingRegistrationCodeOutcome Outcome,
    PendingRegistration? Row);
