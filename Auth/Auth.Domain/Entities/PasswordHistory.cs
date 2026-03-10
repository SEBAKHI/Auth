using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a historical password record for preventing password reuse.
/// </summary>
public class PasswordHistory : EntityBase
{
    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the Argon2id hash of the historical password.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this password was set.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private PasswordHistory() : base()
    {
    }

    public PasswordHistory(
        Guid id,
        Guid userId,
        string passwordHash,
        DateTime createdAt) : base(id)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public static PasswordHistory Create(Guid userId, string passwordHash)
    {
        return new PasswordHistory
        {
            UserId = userId,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };
    }
}
