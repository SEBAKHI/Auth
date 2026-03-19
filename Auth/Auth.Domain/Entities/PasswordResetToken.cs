using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a password reset token for self-service password recovery.
/// </summary>
public class PasswordResetToken : EntityBase
{
    /// <summary>
    /// Gets the ID of the user requesting the password reset.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the Argon2id hash of the reset token.
    /// The actual token is sent to the user; only the hash is stored.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this token expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this token was used (null if not yet used).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this token was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets whether this token is valid (not expired and not used).
    /// </summary>
    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow;

    /// <summary>
    /// Gets whether this token has been used.
    /// </summary>
    public bool IsUsed => UsedAt.HasValue;

    /// <summary>
    /// Gets whether this token has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt <= DateTime.UtcNow;

    private PasswordResetToken() : base()
    {
    }

    public PasswordResetToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        DateTime? usedAt,
        DateTime createdAt) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new password reset token.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tokenHash">The Argon2id hash of the token.</param>
    /// <param name="expirationMinutes">Token expiration in minutes.</param>
    public static PasswordResetToken Create(
        Guid userId,
        string tokenHash,
        int expirationMinutes = 60)
    {
        var now = DateTime.UtcNow;
        return new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = now.AddMinutes(expirationMinutes),
            UsedAt = null,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Marks this token as used.
    /// </summary>
    public ErrorOr<Success> MarkAsUsed()
    {
        if (UsedAt.HasValue)
        {
            return PasswordResetErrors.TokenAlreadyUsed;
        }

        UsedAt = DateTime.UtcNow;
        return Result.Success;
    }
}
