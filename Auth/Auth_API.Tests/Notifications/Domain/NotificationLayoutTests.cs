using Auth.Domain.Entities;
using Auth.Domain.Enums;

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
            "<html>{{ content | raw }}</html>", draftStringsJson, _userId).Value;
        layout.Publish(_userId);
        return layout;
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

        layout.UpdateDraft("Default", layout.DraftContent, CompactStringsReordered, _userId);

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
            _userId);

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_ContentChange_IsTrue()
    {
        var layout = CreatePublishedLayout();

        layout.UpdateDraft(
            "Default", "<html><b>changed</b>{{ content | raw }}</html>", PrettyStrings, _userId);

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_NeverPublished_IsTrue()
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default",
            "<html>{{ content | raw }}</html>", "{}", _userId).Value;

        layout.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void HasUnpublishedChanges_NameOnlyChange_IsFalse()
    {
        // The name is live metadata, not part of the published columns; renaming
        // alone must not suggest the message chrome diverged.
        var layout = CreatePublishedLayout();

        layout.UpdateDraft("Renamed", layout.DraftContent, layout.DraftStringsJson, _userId);

        layout.HasUnpublishedChanges.Should().BeFalse();
    }
}
