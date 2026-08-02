using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// One client display preference belonging to one user — a table's column
/// layout, for example.
///
/// Deliberately narrow: this is presentation state the client owns, not domain
/// state. It carries no behaviour beyond replacing its own value, and nothing
/// on the server reads it. The value stays an opaque JSON string so the shape
/// can change with the UI without a schema change; what the server does
/// guarantee is the <see cref="Key"/> namespace and the size limits, which are
/// enforced on write.
/// </summary>
public class UserUiPreference : EntityBase
{
    /// <summary>Longest accepted key, matching the column width.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>Longest accepted value, matching the column width.</summary>
    public const int MaxValueLength = 4000;

    /// <summary>
    /// The only key namespace currently issued. An allow-list rather than free
    /// keys: without it an authenticated caller can store arbitrary named blobs.
    /// </summary>
    public const string TableKeyPrefix = "table:";

    /// <summary>Most keys one user may hold, so the store cannot be farmed.</summary>
    public const int MaxKeysPerUser = 100;

    /// <summary>Gets the owning user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the preference key, unique per user.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Gets the opaque JSON value.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>Gets when the value was last written.</summary>
    public DateTime ModifiedAt { get; private set; }

    private UserUiPreference()
    {
    }

    public UserUiPreference(Guid id, Guid userId, string key, string value, DateTime modifiedAt)
        : base(id)
    {
        UserId = userId;
        Key = key;
        Value = value;
        ModifiedAt = modifiedAt;
    }

    /// <summary>Creates a preference stamped with the current time.</summary>
    public static UserUiPreference Create(Guid userId, string key, string value) =>
        new(Guid.NewGuid(), userId, key, value, DateTime.UtcNow);

    /// <summary>
    /// Replaces the stored value. Last write wins: two devices disagreeing
    /// about a column layout is not a conflict worth surfacing to the user.
    /// </summary>
    public void SetValue(string value)
    {
        Value = value;
        ModifiedAt = DateTime.UtcNow;
    }
}
