using System.Text.RegularExpressions;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// Value object representing a validated phone number.
/// Accepts digits, spaces, +, -, parentheses, and dots.
/// </summary>
public sealed partial class PhoneNumber : IEquatable<PhoneNumber>
{
    /// <summary>
    /// Gets the phone number value.
    /// </summary>
    public string Value { get; }

    private PhoneNumber(string value) => Value = value;

    /// <summary>
    /// Creates a validated PhoneNumber from a raw string.
    /// </summary>
    public static ErrorOr<PhoneNumber> Create(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return PhoneNumberErrors.Required;

        phoneNumber = phoneNumber.Trim();

        if (phoneNumber.Length > 20)
            return PhoneNumberErrors.TooLong;

        if (!PhoneRegex().IsMatch(phoneNumber))
            return PhoneNumberErrors.InvalidFormat;

        var digitCount = phoneNumber.Count(char.IsDigit);
        if (digitCount < 7)
            return PhoneNumberErrors.TooFewDigits;

        return new PhoneNumber(phoneNumber);
    }

    /// <summary>
    /// Creates a PhoneNumber from a trusted source (database reconstruction). Skips validation.
    /// </summary>
    public static PhoneNumber From(string value) => new(value);

    /// <summary>
    /// Creates a PhoneNumber from a nullable string. Returns null if the input is null.
    /// </summary>
    public static PhoneNumber? FromNullable(string? value) => value is not null ? new PhoneNumber(value) : null;

    public static implicit operator string?(PhoneNumber? phone) => phone?.Value;

    public bool Equals(PhoneNumber? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PhoneNumber other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(PhoneNumber? left, PhoneNumber? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(PhoneNumber? left, PhoneNumber? right) => !(left == right);

    [GeneratedRegex(@"^[\d\s\+\-\(\)\.]+$")]
    private static partial Regex PhoneRegex();
}
