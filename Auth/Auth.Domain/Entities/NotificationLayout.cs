using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// The shared visual identity (full Liquid HTML document with a content slot) for
/// one (application, channel) scope. All languages of every template share the same
/// layout; direction (LTR/RTL) is injected at render time from the language.
/// Layouts use draft/published column pairs rather than version history: publish
/// copies the draft columns in one atomic update.
/// </summary>
public partial class NotificationLayout : AggregateRoot
{
    /// <summary>
    /// Gets the owning application, or null for the global default layout.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the delivery channel this layout targets.
    /// </summary>
    public NotificationChannelType Channel { get; private set; }

    /// <summary>
    /// Gets the admin-facing display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the draft Liquid HTML document. Placeholders: {{ content | raw }},
    /// {{ dir }}, {{ lang }}, {{ strings.* | raw }}, {{ SenderName }}.
    /// </summary>
    public string DraftContent { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the draft per-language chrome strings as JSON:
    /// { "&lt;lang&gt;": { "footer": "..." } }.
    /// </summary>
    public string DraftStringsJson { get; private set; } = "{}";

    /// <summary>
    /// Gets the live layout document, or null when never published.
    /// </summary>
    public string? PublishedContent { get; private set; }

    public string? PublishedStringsJson { get; private set; }

    public DateTime? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }

    /// <summary>
    /// Gets the draft card colour for light mode, or null for the default. The email logo's
    /// background is built in this colour, so it must be the colour behind the logo.
    /// </summary>
    public CardColor? DraftCardColorLight { get; private set; }

    /// <summary>Gets the draft card colour for dark mode, or null for the default.</summary>
    public CardColor? DraftCardColorDark { get; private set; }

    /// <summary>
    /// Gets the live card colours. Versioned with the content on purpose: a colour that went
    /// live on save, while the card it describes still sat in the draft, would put every
    /// message sent in between inside a visible box.
    /// </summary>
    public CardColor? PublishedCardColorLight { get; private set; }
    public CardColor? PublishedCardColorDark { get; private set; }

    /// <summary>Gets the draft colours with every unset one resolved to its default.</summary>
    public CardColors DraftCardColors => CardColors.Of(DraftCardColorLight, DraftCardColorDark);

    /// <summary>Gets the live colours with every unset one resolved to its default.</summary>
    public CardColors PublishedCardColors => CardColors.Of(PublishedCardColorLight, PublishedCardColorDark);

    public bool IsPublished => PublishedContent is not null;

    public bool HasUnpublishedChanges =>
        !IsPublished ||
        !string.Equals(DraftContent, PublishedContent, StringComparison.Ordinal) ||
        !StringsJsonEquivalent(DraftStringsJson, PublishedStringsJson) ||
        DraftCardColorLight != PublishedCardColorLight ||
        DraftCardColorDark != PublishedCardColorDark;

    /// <summary>
    /// Semantic comparison of the chrome-strings JSON. The draft is re-serialized
    /// by editors (compact vs pretty-printed), so a textual comparison would flag
    /// "unpublished changes" forever after a no-op save; only value differences count.
    /// </summary>
    private static bool StringsJsonEquivalent(string? draft, string? published)
    {
        if (string.Equals(draft, published, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return JsonNode.DeepEquals(
                draft is null ? null : JsonNode.Parse(draft),
                published is null ? null : JsonNode.Parse(published));
        }
        catch (JsonException)
        {
            // Unparseable on either side: fall back to the textual verdict (different).
            return false;
        }
    }

    private NotificationLayout() : base()
    {
    }

    public NotificationLayout(
        Guid id,
        Guid? applicationId,
        NotificationChannelType channel,
        string name,
        string draftContent,
        string draftStringsJson,
        string? publishedContent,
        string? publishedStringsJson,
        DateTime? publishedAt,
        Guid? publishedBy,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy,
        CardColor? draftCardColorLight = null,
        CardColor? draftCardColorDark = null,
        CardColor? publishedCardColorLight = null,
        CardColor? publishedCardColorDark = null) : base(id)
    {
        ApplicationId = applicationId;
        Channel = channel;
        Name = name;
        DraftContent = draftContent;
        DraftStringsJson = draftStringsJson;
        PublishedContent = publishedContent;
        PublishedStringsJson = publishedStringsJson;
        PublishedAt = publishedAt;
        PublishedBy = publishedBy;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
        DraftCardColorLight = draftCardColorLight;
        DraftCardColorDark = draftCardColorDark;
        PublishedCardColorLight = publishedCardColorLight;
        PublishedCardColorDark = publishedCardColorDark;
    }

    /// <summary>
    /// Creates a new (unpublished) layout for an (application, channel) scope.
    /// </summary>
    public static ErrorOr<NotificationLayout> Create(
        Guid? applicationId,
        NotificationChannelType channel,
        string name,
        string draftContent,
        string draftStringsJson,
        CardColor? draftCardColorLight,
        CardColor? draftCardColorDark,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(draftContent))
        {
            return NotificationErrors.LayoutContentRequired;
        }

        var layout = new NotificationLayout
        {
            ApplicationId = applicationId,
            Channel = channel,
            Name = name.Trim(),
            DraftContent = draftContent,
            DraftStringsJson = string.IsNullOrWhiteSpace(draftStringsJson) ? "{}" : draftStringsJson,
            DraftCardColorLight = draftCardColorLight,
            DraftCardColorDark = draftCardColorDark
        };
        layout.SetCreated(createdBy);
        return layout;
    }

    /// <summary>
    /// Updates the draft content and chrome strings (does not affect the live layout).
    /// </summary>
    public ErrorOr<Success> UpdateDraft(
        string name,
        string draftContent,
        string draftStringsJson,
        CardColor? draftCardColorLight,
        CardColor? draftCardColorDark,
        Guid userId)
    {
        if (string.IsNullOrWhiteSpace(draftContent))
        {
            return NotificationErrors.LayoutContentRequired;
        }

        Name = name.Trim();
        DraftContent = draftContent;
        DraftStringsJson = string.IsNullOrWhiteSpace(draftStringsJson) ? "{}" : draftStringsJson;
        DraftCardColorLight = draftCardColorLight;
        DraftCardColorDark = draftCardColorDark;
        SetModified(userId);
        return Result.Success;
    }

    /// <summary>
    /// Verifies that the draft writes the card colour behind each logo it shows, given the
    /// draft as rendered (so a colour supplied through a variable counts as written).
    /// </summary>
    /// <remarks>
    /// The logo's background is part of the image and is built in the declared colour. A
    /// layout that changes its card but not the declared colour publishes cleanly and then
    /// frames the logo in a box in every message; this catches that before it goes live.
    /// A colour can only be checked where the layout shows the matching logo.
    /// <para>
    /// Comments are not counted: a colour named only in a CSS or HTML comment paints nothing,
    /// and a comment that prints the colour variable would otherwise satisfy the check for
    /// every colour. Outlook conditional comments (<c>&lt;!--[if …]&gt;</c>) are kept, because
    /// Outlook renders what they hold.
    /// </para>
    /// </remarks>
    public ErrorOr<Success> CheckLogoBackgrounds(string renderedDraft)
    {
        var colours = DraftCardColors;
        var painted = PaintedMarkup(renderedDraft);
        if (DraftContent.Contains(LightLogoVariable, StringComparison.Ordinal) && !colours.Light.AppearsIn(painted))
        {
            return NotificationErrors.LayoutCardColorNotInLayout(colours.Light.Value);
        }

        if (DraftContent.Contains(DarkLogoVariable, StringComparison.Ordinal) && !colours.Dark.AppearsIn(painted))
        {
            return NotificationErrors.LayoutCardColorNotInLayout(colours.Dark.Value);
        }

        return Result.Success;
    }

    private const string LightLogoVariable = "Platform.EmailLogoUrl";
    private const string DarkLogoVariable = "Platform.EmailLogoDarkUrl";

    /// <summary>The rendered layout without its CSS and HTML comments.</summary>
    private static string PaintedMarkup(string html) =>
        HtmlCommentRegex().Replace(CssCommentRegex().Replace(html, string.Empty), string.Empty);

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex CssCommentRegex();

    // An Outlook conditional comment (<!--[if mso]> … <![endif]-->) holds markup Outlook
    // renders, and the "<!-->" that closes <!--[if !mso]><!--> opens nothing: a match
    // starting there would run to the next "-->" and swallow the markup in between.
    [GeneratedRegex(@"<!--(?!\[if|>)[\s\S]*?-->")]
    private static partial Regex HtmlCommentRegex();

    /// <summary>
    /// Verifies that the saved draft is the exact revision reviewed by the caller.
    /// </summary>
    public ErrorOr<Success> ValidatePublishTarget(DateTime expectedRevisionAt)
    {
        return (ModifiedAt ?? CreatedAt) == expectedRevisionAt
            ? Result.Success
            : NotificationErrors.LayoutPublishTargetChanged;
    }

    /// <summary>
    /// Publishes the saved draft only when it is the exact revision reviewed by
    /// the caller.
    /// </summary>
    public ErrorOr<Success> Publish(DateTime expectedRevisionAt, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(DraftContent))
        {
            return NotificationErrors.LayoutContentRequired;
        }

        var targetResult = ValidatePublishTarget(expectedRevisionAt);
        if (targetResult.IsError)
        {
            return targetResult.Errors;
        }

        PublishedContent = DraftContent;
        PublishedStringsJson = DraftStringsJson;
        PublishedCardColorLight = DraftCardColorLight;
        PublishedCardColorDark = DraftCardColorDark;
        PublishedAt = DateTime.UtcNow;
        PublishedBy = userId;
        SetModified(userId);
        return Result.Success;
    }

    /// <summary>
    /// Whether this layout may be deleted. An application's layout may: its emails then fall
    /// back to the global layout of the same channel, which the renderer already resolves for
    /// an application that has none. The global layout of a channel may not, because nothing
    /// would wrap that channel's emails any more and every send on it would fail.
    /// </summary>
    public ErrorOr<Success> EnsureDeletable() =>
        ApplicationId is null ? NotificationErrors.CannotDeleteGlobalLayout : Result.Success;
}
