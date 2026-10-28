namespace Auth.Domain.ValueObjects;

/// <summary>
/// The pair of card colours an email layout is drawn with: one for light mode, one for dark.
/// A colour the layout leaves unset resolves to the shipped default here, so every reader
/// gets a complete pair.
/// </summary>
public sealed record class CardColors
{
    /// <summary>The pair the shipped layout is drawn with.</summary>
    public static CardColors Default { get; } = new(CardColor.DefaultLight, CardColor.DefaultDark);

    public CardColor Light { get; }
    public CardColor Dark { get; }

    private CardColors(CardColor light, CardColor dark)
    {
        Light = light;
        Dark = dark;
    }

    /// <summary>Resolves a layout's optional colours, each unset one to its default.</summary>
    public static CardColors Of(CardColor? light, CardColor? dark) =>
        new(light ?? CardColor.DefaultLight, dark ?? CardColor.DefaultDark);
}
