using Auth.Domain.Enums;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a short-lived login-time two-factor challenge issued after
/// successful password (or external provider) verification. The client holds
/// an opaque high-entropy token; only its keyed hash is stored.
/// </summary>
public class TwoFactorChallenge : EntityBase
{
    /// <summary>
    /// Maximum number of code verification attempts allowed per challenge.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Default challenge lifetime in minutes.
    /// </summary>
    public const int DefaultLifetimeMinutes = 5;

    /// <summary>
    /// The most codes one challenge may email (S24). Each send replaces the live code
    /// and restarts its lifetime; the earlier ones stay recognisable as superseded.
    /// </summary>
    public const int MaxEmailCodeSends = 3;

    /// <summary>
    /// The separator between the code hashes in <see cref="EmailCodeHash"/>. A hash is
    /// base64 or base64url, so it never contains one.
    /// </summary>
    public const char EmailCodeHashSeparator = ';';

    /// <summary>
    /// Gets the ID of the user this challenge belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the keyed HMAC-SHA256 hash of the opaque challenge token.
    /// The actual token is returned to the client; only the hash is stored.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the IP address that initiated the login, if known.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this challenge expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this challenge was used (null if not yet used).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the number of verification attempts reserved on this challenge. Each
    /// attempt is reserved before its code is checked; a correct code gives its
    /// reservation back when it consumes the challenge, so at rest the count is
    /// the number of rejected codes — which is what the sign-in history shows.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this challenge was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the first factor that opened this challenge — the password, an
    /// external identity, or an emailed code — so the verify step can record the
    /// whole sign-in. <see cref="AuthenticationMethods.Unknown"/> for a challenge
    /// issued before the column existed.
    /// </summary>
    public AuthenticationMethods PrimaryMethod { get; private set; }

    /// <summary>
    /// Gets what this challenge is for. Every statement that reads or spends it
    /// names the purpose it serves, so a row opened for one purpose never answers
    /// another.
    /// </summary>
    public TwoFactorChallengePurpose Purpose { get; private set; } = TwoFactorChallengePurpose.SignIn;

    /// <summary>
    /// Gets the keyed hashes of every code emailed for this challenge, oldest first,
    /// joined by <see cref="EmailCodeHashSeparator"/>, exactly as stored; null when
    /// none was sent. The last one is the live code.
    /// </summary>
    public string? EmailCodeHash { get; private set; }

    /// <summary>
    /// Gets when the last code was emailed for this challenge.
    /// </summary>
    public DateTime? EmailCodeSentAt { get; private set; }

    /// <summary>
    /// Gets how many codes this challenge emailed.
    /// </summary>
    public int EmailCodeSendCount { get; private set; }

    /// <summary>
    /// Gets the hash of the live emailed code: the newest one. Null when none was sent.
    /// </summary>
    public string? LiveEmailCodeHash => EmailCodeHashes.LastOrDefault();

    /// <summary>
    /// Gets the hashes of the codes a newer send replaced, oldest first.
    /// </summary>
    public IReadOnlyList<string> SupersededEmailCodeHashes =>
        EmailCodeHashes.Count > 1 ? EmailCodeHashes.Take(EmailCodeHashes.Count - 1).ToList() : [];

    private IReadOnlyList<string> EmailCodeHashes =>
        string.IsNullOrEmpty(EmailCodeHash)
            ? []
            : EmailCodeHash.Split(EmailCodeHashSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Gets whether this challenge is valid (not expired, not used, and attempts not exceeded).
    /// </summary>
    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow && AttemptCount < MaxAttempts;

    private TwoFactorChallenge() : base()
    {
    }

    public TwoFactorChallenge(
        Guid id,
        Guid userId,
        string tokenHash,
        string? ipAddress,
        DateTime expiresAt,
        DateTime? usedAt,
        int attemptCount,
        DateTime createdAt,
        AuthenticationMethods primaryMethod = default,
        TwoFactorChallengePurpose purpose = TwoFactorChallengePurpose.SignIn,
        string? emailCodeHash = null,
        DateTime? emailCodeSentAt = null,
        int emailCodeSendCount = 0) : base(id)
    {
        PrimaryMethod = primaryMethod;
        Purpose = purpose;
        EmailCodeHash = emailCodeHash;
        EmailCodeSentAt = emailCodeSentAt;
        EmailCodeSendCount = emailCodeSendCount;
        UserId = userId;
        TokenHash = tokenHash;
        IpAddress = ipAddress;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new two-factor login challenge.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tokenHash">The keyed hash of the opaque challenge token.</param>
    /// <param name="ipAddress">The IP address that initiated the login.</param>
    /// <param name="lifetimeMinutes">Challenge lifetime in minutes (default 5).</param>
    /// <param name="primaryMethod">The first factor the sign-in proved before this challenge.</param>
    /// <param name="purpose">What the challenge is for; a sign-in unless named.</param>
    public static TwoFactorChallenge Create(
        Guid userId,
        string tokenHash,
        string? ipAddress,
        int lifetimeMinutes = DefaultLifetimeMinutes,
        AuthenticationMethods primaryMethod = default,
        TwoFactorChallengePurpose purpose = TwoFactorChallengePurpose.SignIn)
    {
        var now = DateTime.UtcNow;
        return new TwoFactorChallenge
        {
            PrimaryMethod = primaryMethod,
            Purpose = purpose,
            UserId = userId,
            TokenHash = tokenHash,
            IpAddress = ipAddress,
            ExpiresAt = now.AddMinutes(lifetimeMinutes),
            UsedAt = null,
            AttemptCount = 0,
            CreatedAt = now
        };
    }
}
