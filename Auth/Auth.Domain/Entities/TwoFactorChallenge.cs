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
        AuthenticationMethods primaryMethod = default) : base(id)
    {
        PrimaryMethod = primaryMethod;
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
    public static TwoFactorChallenge Create(
        Guid userId,
        string tokenHash,
        string? ipAddress,
        int lifetimeMinutes = DefaultLifetimeMinutes,
        AuthenticationMethods primaryMethod = default)
    {
        var now = DateTime.UtcNow;
        return new TwoFactorChallenge
        {
            PrimaryMethod = primaryMethod,
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
