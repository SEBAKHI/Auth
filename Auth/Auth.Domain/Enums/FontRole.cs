namespace Auth.Domain.Enums;

/// <summary>
/// What a platform font is used for. The platform holds at most one font file per role.
/// </summary>
public enum FontRole
{
    /// <summary>Titles: of cards, dialogs, sheets and empty states, and the headings of sign-in page designs.</summary>
    Heading = 0,

    /// <summary>Every other text, and what a title falls back to while no heading font is chosen.</summary>
    Body = 1
}
