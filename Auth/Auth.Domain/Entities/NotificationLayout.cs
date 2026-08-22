using System.Text.Json;
using System.Text.Json.Nodes;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// The shared visual identity (full Liquid HTML document with a content slot) for
/// one (application, channel) scope. All languages of every template share the same
/// layout; direction (LTR/RTL) is injected at render time from the language.
/// Layouts use draft/published column pairs rather than version history: publish
/// copies the draft columns in one atomic update.
/// </summary>
public class NotificationLayout : AggregateRoot
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

    public bool IsPublished => PublishedContent is not null;

    public bool HasUnpublishedChanges =>
        !IsPublished ||
        !string.Equals(DraftContent, PublishedContent, StringComparison.Ordinal) ||
        !StringsJsonEquivalent(DraftStringsJson, PublishedStringsJson);

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
        Guid? modifiedBy) : base(id)
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
            DraftStringsJson = string.IsNullOrWhiteSpace(draftStringsJson) ? "{}" : draftStringsJson
        };
        layout.SetCreated(createdBy);
        return layout;
    }

    /// <summary>
    /// Updates the draft content and chrome strings (does not affect the live layout).
    /// </summary>
    public ErrorOr<Success> UpdateDraft(string name, string draftContent, string draftStringsJson, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(draftContent))
        {
            return NotificationErrors.LayoutContentRequired;
        }

        Name = name.Trim();
        DraftContent = draftContent;
        DraftStringsJson = string.IsNullOrWhiteSpace(draftStringsJson) ? "{}" : draftStringsJson;
        SetModified(userId);
        return Result.Success;
    }

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
        PublishedAt = DateTime.UtcNow;
        PublishedBy = userId;
        SetModified(userId);
        return Result.Success;
    }
}
