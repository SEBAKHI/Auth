using System.Globalization;
using System.Text.RegularExpressions;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The background colour of an email layout's card, always an opaque <c>#RRGGBB</c>.
/// </summary>
/// <remarks>
/// The email logo is a PNG whose background is part of the image, because mail clients
/// recolour CSS backgrounds but never image pixels. That background is invisible only while
/// it equals the card behind the logo, and the card belongs to the layout, so the colour
/// belongs to the layout too. There is deliberately no form with an alpha channel: a
/// see-through background is exactly what the baked-in background exists to prevent.
/// </remarks>
public sealed partial class CardColor : IEquatable<CardColor>
{
    /// <summary>The light card of the shipped layout. A layout that sets no colour uses it.</summary>
    public static CardColor DefaultLight { get; } = new("#FFFFFF");

    /// <summary>The dark card of the shipped layout. A layout that sets no colour uses it.</summary>
    public static CardColor DefaultDark { get; } = new("#1A1A1C");

    /// <summary>Gets the colour as <c>#RRGGBB</c> in upper case.</summary>
    public string Value { get; }

    private CardColor(string value) => Value = value;

    public byte Red => Channel(1);
    public byte Green => Channel(3);
    public byte Blue => Channel(5);

    /// <summary>
    /// Creates a colour from <c>#RRGGBB</c> (either case). Shorthand and alpha forms are refused.
    /// </summary>
    public static ErrorOr<CardColor> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return HexRegex().IsMatch(trimmed)
            ? new CardColor(trimmed.ToUpperInvariant())
            : NotificationErrors.LayoutCardColorInvalid;
    }

    /// <summary>
    /// Creates an optional colour: empty means "not set", so the layout uses the default.
    /// </summary>
    public static ErrorOr<CardColor?> CreateOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (CardColor?)null;
        }

        var created = Create(value);
        return created.IsError ? created.Errors : created.Value;
    }

    /// <summary>
    /// Rebuilds a colour from a trusted source (the database, which holds the same CHECK).
    /// </summary>
    public static CardColor? FromNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new CardColor(value.Trim().ToUpperInvariant());

    /// <summary>
    /// True when the HTML writes this colour, as <c>#RRGGBB</c> or as its <c>#RGB</c>
    /// shorthand, in either case.
    /// </summary>
    /// <remarks>
    /// The check a publish needs: a layout that shows the logo but never writes the colour the
    /// logo's background was built with has almost certainly changed its card without changing
    /// the colour, and every message would show the logo inside a visible box.
    /// </remarks>
    public bool AppearsIn(string html)
    {
        if (Occurs(html, Value))
        {
            return true;
        }

        return Shorthand() is { } shorthand && Occurs(html, shorthand);
    }

    private string? Shorthand() =>
        Value[1] == Value[2] && Value[3] == Value[4] && Value[5] == Value[6]
            ? $"#{Value[1]}{Value[3]}{Value[5]}"
            : null;

    // A match followed by another hex digit is a longer colour (#FFF inside #FFFFFF), not this one.
    private static bool Occurs(string html, string colour)
    {
        for (var index = html.IndexOf(colour, StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = html.IndexOf(colour, index + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = index + colour.Length;
            if (end == html.Length || !char.IsAsciiHexDigit(html[end]))
            {
                return true;
            }
        }

        return false;
    }

    private byte Channel(int start) =>
        byte.Parse(Value.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public bool Equals(CardColor? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is CardColor other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(CardColor? left, CardColor? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(CardColor? left, CardColor? right) => !(left == right);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRegex();
}
