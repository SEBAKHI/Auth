using System.Text.RegularExpressions;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// Value object representing a permission code in the hierarchical permission system.
/// Supports colon-separated hierarchy (e.g., "crm:leads:read") and wildcard patterns ("crm:*", "*").
/// </summary>
public sealed partial class PermissionCode : IEquatable<PermissionCode>
{
    /// <summary>
    /// Gets the permission code value (lowercase).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the hierarchy level: 0=global(*), 1=application, 2=resource, 3=action.
    /// </summary>
    public byte Level { get; }

    /// <summary>
    /// Gets whether this is a wildcard permission (ends with :* or is just *).
    /// </summary>
    public bool IsWildcard { get; }

    private PermissionCode(string value)
    {
        Value = value;
        Level = CalculateLevel(value);
        IsWildcard = value == "*" || value.EndsWith(":*");
    }

    /// <summary>
    /// Creates a validated PermissionCode from a raw string.
    /// Trims whitespace and normalizes to lowercase.
    /// </summary>
    public static ErrorOr<PermissionCode> Create(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("PermissionCode.Empty", "Permission code cannot be empty.");

        code = code.Trim().ToLowerInvariant();

        if (code.Length > 200)
            return Error.Validation("PermissionCode.TooLong", "Permission code cannot exceed 200 characters.");

        if (!CodeRegex().IsMatch(code))
            return Error.Validation("PermissionCode.InvalidFormat",
                "Permission code must contain only lowercase letters, digits, colons, and asterisks.");

        return new PermissionCode(code);
    }

    /// <summary>
    /// Creates a PermissionCode from a trusted source (database reconstruction). Skips validation.
    /// </summary>
    public static PermissionCode From(string value) => new(value);

    /// <summary>
    /// Checks if this permission code matches the required permission using wildcard logic.
    /// </summary>
    /// <param name="requiredPermission">The permission code to check against.</param>
    /// <returns>True if this permission grants access to the required permission.</returns>
    public bool Matches(string requiredPermission)
    {
        // Global wildcard grants everything
        if (Value == "*")
            return true;

        // Exact match
        if (string.Equals(Value, requiredPermission, StringComparison.OrdinalIgnoreCase))
            return true;

        // Wildcard matching (e.g., "crm:*" matches "crm:leads:read")
        if (Value.EndsWith(":*"))
        {
            var prefix = Value[..^2]; // Remove ":*"
            return requiredPermission.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(requiredPermission, prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Gets the parent permission code (e.g., "crm:leads:read" -> "crm:leads:*").
    /// </summary>
    public string? GetParentCode()
    {
        if (Value == "*") return null;

        var lastColon = Value.LastIndexOf(':');
        if (lastColon <= 0) return "*";

        return Value[..lastColon] + ":*";
    }

    private static byte CalculateLevel(string code)
    {
        if (code == "*") return 0;
        return (byte)(code.Count(c => c == ':') + 1);
    }

    public static implicit operator string(PermissionCode code) => code.Value;

    public bool Equals(PermissionCode? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PermissionCode other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(PermissionCode? left, PermissionCode? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(PermissionCode? left, PermissionCode? right) => !(left == right);

    [GeneratedRegex(@"^[a-z0-9:*_\-]+$")]
    private static partial Regex CodeRegex();
}
