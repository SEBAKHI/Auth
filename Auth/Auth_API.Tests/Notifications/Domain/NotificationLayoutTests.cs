using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Notifications.Domain;

/// <summary>
/// Unit tests for the <see cref="NotificationLayout"/> aggregate, focused on the
/// unpublished-changes verdict: it must reflect value differences, not JSON
/// serialization formatting (editors re-serialize the strings compactly while
/// the seed is pretty-printed).
/// </summary>
public class NotificationLayoutTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private const string PrettyStrings = """
        {
        "en": {"footer": "Automated message from {{ SenderName }}."},
        "ar": {"footer": "رسالة تلقائية من {{ SenderName }}."}
        }
        """;

    // Same values, compact serialization, different key order.
    private const string CompactStringsReordered =
        """{"ar":{"footer":"رسالة تلقائية من {{ SenderName }}."},"en":{"footer":"Automated message from {{ SenderName }}."}}""";

    private NotificationLayout CreatePublishedLayout(string draftStringsJson = PrettyStrings)
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            "<html>{{ content | raw }}</html>", draftStringsJson, null, null, _userId).Value;
        layout.Publish(layout.ModifiedAt ?? layout.CreatedAt, _userId);
        return layout;
    }

    [Fact]
    public void CardColours_ChangeOnlyTheDraftUntilPublished()
    {
        // A colour that went live on save, while the card it describes still sat in the
        // draft, would box the logo in every message sent in between.
        var layout = CreatePublishedLayout();
        var customCard = CardColor.Create("#3C6E47").Value;

        layout.UpdateDraft("Default", layout.DraftContent, layout.DraftStringsJson, null, customCard, _userId);

        layout.HasUnpublishedChanges.Should().BeTrue();
        layout.DraftCardColors.Dark.Should().Be(customCard);
        layout.PublishedCardColors.Should().Be(CardColors.Default);

        layout.Publish(layout.ModifiedAt ?? layout.CreatedAt, _userId);

        layout.PublishedCardColors.Dark.Should().Be(customCard);
        layout.HasUnpublishedChanges.Should().BeFalse();
    }

    [Theory]
    [InlineData("<img src=\"{{ Platform.EmailLogoDarkUrl }}\">", "<style>.card{background:#3C6E47}</style>", false)]
    [InlineData("<img src=\"{{ Platform.EmailLogoDarkUrl }}\">", "<style>.card{background:#1A1A1C}</style>", true)]
    [InlineData("<p>No logo at all</p>", "<style>.card{background:#3C6E47}</style>", true)]
    public void CheckLogoBackgrounds_RequiresTheColourBehindEachLogoShown(string logo, string rendered, bool passes)
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            $"<html>{logo}{{{{ content | raw }}}}</html>", "{}", null, null, _userId).Value;

        var result = layout.CheckLogoBackgrounds(rendered);

        result.IsError.Should().Be(!passes);
        if (!passes)
        {
            result.FirstError.Code.Should().Be("Notification.LayoutCardColorNotInLayout");
        }
    }

    [Theory]
    // A colour named only in a comment paints nothing, so it does not count.
    [InlineData("<style>/* dark card #1A1A1C */ .card{background:#3C6E47}</style>", false)]
    [InlineData("<!-- the dark card is #1A1A1C --><style>.card{background:#3C6E47}</style>", false)]
    // "<!-->" closes <!--[if !mso]><!--> and opens nothing; what follows it is painted.
    [InlineData("<!--[if !mso]><!--><td bgcolor=\"#1A1A1C\"></td><!--<![endif]-->", true)]
    // Outlook renders what its conditional comment holds.
    [InlineData("<!--[if mso]><td bgcolor=\"#1A1A1C\"></td><![endif]-->", true)]
    [InlineData("<style>.card{background:#1A1A1C}</style>", true)]
    public void CheckLogoBackgrounds_CountsOnlyColoursOutsideComments(string rendered, bool passes)
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            "<html><img src=\"{{ Platform.EmailLogoDarkUrl }}\">{{ content | raw }}</html>", "{}", null, null, _userId).Value;

        layout.CheckLogoBackgrounds(rendered).IsError.Should().Be(!passes);
    }

    [Fact]
    public void HasUnpublishedChanges_AfterPublish_IsFalse()
    {
        var layout = CreatePublishedLayout();

        layout.HasUnpublishedChanges.Should().BeFalse();
    }

    [Fact]
    public void HasUnpublishedChanges_ReserializedIdenticalStrings_IsFalse()
    {
        // A no-op save that only re-serializes the JSON (compact, reordered)
        // must NOT flag unpublished changes — the values are identical.
        var layout = CreatePublishedLayout();

        layout.UpdateDraft("Default", layout.DraftContent, CompactStringsReordered, null, null, _userId);

        layout.HasUnpublishedChanges.Should().BeFalse();
    }

    [Fact]
    public void HasUnpublishedChanges_ActualStringValueChange_IsTrue()
    {
        var layout = CreatePublishedLayout();

        layout.UpdateDraft(
            "Default",
            layout.DraftContent,
            """{"en":{"footer":"A different footer."}}""",
            null,
            null,
            _userId);

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_ContentChange_IsTrue()
    {
        var layout = CreatePublishedLayout();

        layout.UpdateDraft(
            "Default", "<html><b>changed</b>{{ content | raw }}</html>", PrettyStrings, null, null, _userId);

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_NeverPublished_IsTrue()
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            "<html>{{ content | raw }}</html>", "{}", null, null, _userId).Value;

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_NameOnlyChange_IsFalse()
    {
        // The name is live metadata, not part of the published columns; renaming
        // alone must not suggest the message chrome diverged.
        var layout = CreatePublishedLayout();

        layout.UpdateDraft("Renamed", layout.DraftContent, layout.DraftStringsJson, null, null, _userId);

        layout.HasUnpublishedChanges.Should().BeFalse();
    }

    [Fact]
    public void Publish_StaleRevision_ReturnsConflictWithoutPublishing()
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            "<html>{{ content | raw }}</html>", "{}", null, null, _userId).Value;

        var result = layout.Publish(layout.CreatedAt.AddMinutes(-1), _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutPublishTargetChanged");
        layout.IsPublished.Should().BeFalse();
    }
}
