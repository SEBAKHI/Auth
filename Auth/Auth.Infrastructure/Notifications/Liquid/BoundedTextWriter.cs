namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// The writer every render, capture and <c>ifchanged</c> body writes into. It refuses a write
/// before copying it when the text would pass its cap, so it never holds more than the cap:
/// the output of a loop is stopped at the cap, not measured after it is built.
/// </summary>
/// <remarks>
/// <see cref="StringWriter"/> keeps its asynchronous members synchronous and routes the span,
/// <see cref="System.Text.StringBuilder"/> and line overloads of a subclass through the four
/// overridden here, so these four see every character.
/// </remarks>
internal sealed class BoundedTextWriter : StringWriter
{
    private readonly RenderBudget _budget;
    private readonly long _maxUtf8Bytes;
    private readonly long _maxCharacters;
    private readonly TemplateLimit _overflow;
    private long _utf8Bytes;

    private BoundedTextWriter(RenderBudget budget, long maxUtf8Bytes, long maxCharacters, TemplateLimit overflow)
        : base(System.Globalization.CultureInfo.InvariantCulture)
    {
        _budget = budget;
        _maxUtf8Bytes = maxUtf8Bytes;
        _maxCharacters = maxCharacters;
        _overflow = overflow;
    }

    /// <summary>
    /// The output of one render: capped at the output cap in UTF-8 bytes (which also caps its
    /// characters), refused as too large.
    /// </summary>
    public static BoundedTextWriter ForOutput(RenderBudget budget) =>
        new(budget, budget.MaxOutputBytes, long.MaxValue, TemplateLimit.OutputTooLarge);

    /// <summary>
    /// A value a template builds by writing (capture, ifchanged): capped at the longest value, in
    /// characters, and refused as too complex since it is not the output.
    /// </summary>
    public static BoundedTextWriter ForValue(RenderBudget budget) =>
        new(budget, long.MaxValue, budget.MaxValueChars, TemplateLimit.TooComplex);

    public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> buffer)
    {
        _budget.Tick();

        var builder = GetStringBuilder();
        _utf8Bytes += Utf8Length(buffer);
        if (_utf8Bytes > _maxUtf8Bytes || builder.Length + buffer.Length > _maxCharacters)
        {
            throw new TemplateLimitException(_overflow);
        }

        _budget.Charge(buffer.Length);
        builder.Append(buffer);
    }

    /// <summary>
    /// UTF-8 bytes of the characters, exact for well-formed UTF-16: a surrogate pair is four
    /// bytes, two per half, so a pair split across two writes still adds up. A lone surrogate
    /// counts two where UTF-8 writes three; the renderer recounts the finished text exactly.
    /// </summary>
    private static int Utf8Length(ReadOnlySpan<char> buffer)
    {
        var bytes = 0;
        foreach (var c in buffer)
        {
            bytes += c switch
            {
                < (char)0x80 => 1,
                < (char)0x800 => 2,
                >= (char)0xD800 and <= (char)0xDFFF => 2,
                _ => 3
            };
        }

        return bytes;
    }
}
