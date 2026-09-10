using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// A self-registration that has not yet earned its account: the address
/// someone typed, the hash of the code mailed to it, and the counters that
/// bound what that code can be spent on. Nothing about the person — no name,
/// no password — lives here; those arrive with the code and go straight into
/// the account row the completion step creates.
/// </summary>
/// <remarks>
/// One row per address at a time (the database enforces it on
/// <see cref="NormalizedEmail"/> while <see cref="ConsumedAt"/> is null).
/// <see cref="ConsumedAt"/> has exactly one meaning: a Users row now exists for
/// this address. Expiry and exhausted attempts never stamp it — a dead code is
/// rotated in place by the next start for the address, keeping the row, its
/// id and its mail counters. Those counters are what bound guessing: codes
/// per window times attempts per code. They belong to the address, so they
/// survive rotation.
/// </remarks>
public class PendingRegistration : AggregateRoot
{
    /// <summary>
    /// Maximum number of wrong codes before the current code is dead. Shared
    /// by the check-only step and the completion step: five guesses per code,
    /// whichever door they come through.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// The opaque value the client holds; a keyed digest of the address,
    /// stable across rotation and consumption. Not a secret.
    /// </summary>
    public string Handle { get; private set; } = string.Empty;

    /// <summary>The address exactly as it will be written to the account (lower-case).</summary>
    public Email Email { get; private set; } = Email.From(string.Empty);

    /// <summary>The address in the form Users.NormalizedEmail stores (upper-case).</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>Keyed hash of the current six-digit code, under <see cref="OtpScope"/>.</summary>
    public string OtpHash { get; private set; } = string.Empty;

    /// <summary>When the current code dies. Enforced on every read, never by the sweep.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Wrong codes presented against the current code.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Codes issued for this address inside the current mail window.</summary>
    public int MailedCount { get; private set; }

    /// <summary>Start of the window <see cref="MailedCount"/> is counted in.</summary>
    public DateTime MailWindowStartUtc { get; private set; }

    /// <summary>
    /// When the current code's message reached the outbox; null means it never
    /// did (the enqueue failed after the row was committed) and the next start
    /// may issue a fresh one without waiting for expiry.
    /// </summary>
    public DateTime? MailedAt { get; private set; }

    /// <summary>
    /// When the current code was first presented correctly. Telemetry only:
    /// the completion step never reads it — the code is the proof, every time.
    /// </summary>
    public DateTime? VerifiedAt { get; private set; }

    /// <summary>When a Users row was created for this address — by any door.</summary>
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>Language of the last request that issued a code; drives the code message.</summary>
    public string? PreferredLanguage { get; private set; }

    /// <summary>
    /// The scope the code is hashed under. Bound to this row's id so a code
    /// minted for a user's e-mail verification (scope: the user id) can never
    /// be presented here, and vice versa, although both use one HMAC key.
    /// Deliberately not the address: an address is caller-typed input inside a
    /// keyed message and would force a normalization decision forever.
    /// </summary>
    public string OtpScope => $"pending-registration:{Id}";

    /// <summary>A Users row exists for this address.</summary>
    public bool IsConsumed => ConsumedAt.HasValue;

    /// <summary>The current code has taken its five wrong guesses.</summary>
    public bool IsExhausted => AttemptCount >= MaxAttempts;

    /// <summary>The current code is past its expiry at <paramref name="nowUtc"/>.</summary>
    public bool IsExpired(DateTime nowUtc) => ExpiresAt <= nowUtc;

    /// <summary>
    /// The current code can still be presented: unconsumed, unexpired, with
    /// attempts remaining. A live code is never rotated or invalidated by a
    /// later start — it is in somebody's inbox.
    /// </summary>
    public bool IsCodeLive(DateTime nowUtc) => !IsConsumed && !IsExpired(nowUtc) && !IsExhausted;

    /// <summary>The current code's message reached the outbox.</summary>
    public bool WasMailed => MailedAt.HasValue;

    private PendingRegistration() : base()
    {
    }

    /// <summary>Rehydrates a stored row.</summary>
    public PendingRegistration(
        Guid id,
        string handle,
        string email,
        string normalizedEmail,
        string otpHash,
        DateTime expiresAt,
        int attemptCount,
        int mailedCount,
        DateTime mailWindowStartUtc,
        DateTime? mailedAt,
        DateTime? verifiedAt,
        DateTime? consumedAt,
        string? preferredLanguage,
        DateTime createdAt) : base(id)
    {
        Handle = handle;
        Email = Email.From(email);
        NormalizedEmail = normalizedEmail;
        OtpHash = otpHash;
        ExpiresAt = expiresAt;
        AttemptCount = attemptCount;
        MailedCount = mailedCount;
        MailWindowStartUtc = mailWindowStartUtc;
        MailedAt = mailedAt;
        VerifiedAt = verifiedAt;
        ConsumedAt = consumedAt;
        PreferredLanguage = preferredLanguage;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// A new row for an address with no unconsumed row. It carries no code
    /// yet: the caller charges the mail window and issues one, exactly as it
    /// would for a rotation, so the first code and every later one take the
    /// same path.
    /// </summary>
    public static PendingRegistration Create(string handle, string email, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var address = Email.From(email.Trim().ToLowerInvariant());

        return new PendingRegistration
        {
            Handle = handle,
            Email = address,
            NormalizedEmail = NormalizeKey(email),
            OtpHash = string.Empty,
            ExpiresAt = nowUtc,
            AttemptCount = 0,
            MailedCount = 0,
            MailWindowStartUtc = nowUtc,
            CreatedAt = nowUtc
        };
    }

    /// <summary>
    /// The one derivation of the key a row is found by: the form
    /// Users.NormalizedEmail stores, reached by the same two steps the row's
    /// own <see cref="Email"/> takes. Upper-casing the raw input directly is
    /// not the same function for a handful of code points, and a lock taken
    /// on one key while the row is written under another is no lock at all.
    /// Every reader — the locked read, the handle derivation — must use this.
    /// </summary>
    public static string NormalizeKey(string email) =>
        Email.From(email.Trim().ToLowerInvariant()).ToNormalized();

    /// <summary>
    /// Points the row at the handle the current key derives for its address.
    /// The platform's HMAC key can be rotated, and a row that keeps being
    /// re-started never expires, so the stored handle is refreshed whenever a
    /// code is issued; between rotations the stored one is what the client
    /// holds and what the lookup uses.
    /// </summary>
    public void Rebind(string handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        Handle = handle;
    }

    /// <summary>
    /// Spends one code from the address's mail window, opening a new window
    /// when the old one has elapsed. False means the cap for this window is
    /// reached and no code may be issued; nothing changes in that case.
    /// </summary>
    /// <remarks>
    /// This is the guessing bound, not a courtesy: at most
    /// <paramref name="maxPerWindow"/> codes per <paramref name="window"/>, each
    /// worth <see cref="MaxAttempts"/> guesses, however many addresses or
    /// clients the caller controls.
    /// </remarks>
    public bool TryChargeMailWindow(TimeSpan window, int maxPerWindow, DateTime nowUtc)
    {
        if (MailWindowStartUtc + window <= nowUtc)
        {
            MailWindowStartUtc = nowUtc;
            MailedCount = 1;
            return true;
        }

        if (MailedCount >= maxPerWindow)
        {
            return false;
        }

        MailedCount++;
        return true;
    }

    /// <summary>
    /// Issues the row's current code: the first one on a new row, or a
    /// rotation in place of a dead one. Attempts, the verified stamp and the
    /// mailed stamp all start over; the id, the handle and the mail counters
    /// do not.
    /// </summary>
    public void IssueCode(string otpHash, int expirationMinutes, string? preferredLanguage, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(otpHash);

        if (IsConsumed)
        {
            throw new InvalidOperationException("A consumed registration cannot be issued a code.");
        }

        OtpHash = otpHash;
        ExpiresAt = nowUtc.AddMinutes(expirationMinutes);
        AttemptCount = 0;
        MailedAt = null;
        VerifiedAt = null;
        PreferredLanguage = preferredLanguage;
    }

    /// <summary>The current code's message reached the outbox.</summary>
    public void MarkMailed(DateTime nowUtc) => MailedAt ??= nowUtc;

    /// <summary>One more wrong code against the current one.</summary>
    public void RecordFailedAttempt() => AttemptCount++;

    /// <summary>The current code was presented correctly (first time only is kept).</summary>
    public void MarkVerified(DateTime nowUtc) => VerifiedAt ??= nowUtc;

    /// <summary>A Users row was created for this address.</summary>
    public void Consume(DateTime nowUtc) => ConsumedAt ??= nowUtc;
}
