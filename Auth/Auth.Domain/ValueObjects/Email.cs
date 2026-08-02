using System.Text.RegularExpressions;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// Value object representing a validated email address.
/// Stores the email in lowercase for consistent comparison.
/// </summary>
public sealed partial class Email : IEquatable<Email>
{
    /// <summary>
    /// Gets the email address value (lowercase).
    /// </summary>
    public string Value { get; }

    private Email(string value) => Value = value;

    /// <summary>
    /// Creates a validated Email from a raw string.
    /// Trims whitespace and normalizes to lowercase.
    /// </summary>
    public static ErrorOr<Email> Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Email.Empty", "Email address cannot be empty.");

        email = email.Trim();

        if (email.Length > 254)
            return Error.Validation("Email.TooLong", "Email address cannot exceed 254 characters.");

        if (!EmailRegex().IsMatch(email))
            return Error.Validation("Email.InvalidFormat", "Email address format is invalid.");

        return new Email(email.ToLowerInvariant());
    }

    /// <summary>
    /// Creates an Email from a trusted source (database reconstruction). Skips validation.
    /// </summary>
    public static Email From(string value) => new(value);

    /// <summary>
    /// Creates an Email from a nullable string. Returns null if the input is null.
    /// </summary>
    public static Email? FromNullable(string? value) => value is not null ? new Email(value) : null;

    /// <summary>
    /// Returns the uppercase normalized form for case-insensitive database lookups.
    /// </summary>
    public string ToNormalized() => Value.ToUpperInvariant();

    /// <summary>
    /// Converts a non-null Email to its underlying string value.
    /// A non-null Email always carries a non-null <see cref="Value"/>; use <c>?.Value</c>
    /// when the Email reference itself may be null.
    /// </summary>
    public static implicit operator string(Email email) => email.Value;

    public bool Equals(Email? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Email other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(Email? left, Email? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(Email? left, Email? right) => !(left == right);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
