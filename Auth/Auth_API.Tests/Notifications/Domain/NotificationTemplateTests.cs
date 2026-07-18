using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;

namespace Auth_API.Tests.Notifications.Domain;

/// <summary>
/// Unit tests for the <see cref="NotificationTemplate"/> aggregate root.
/// Covers the pointer versioning invariants: publish, unpublish, rollback,
/// draft lifecycle, and translation atomicity guarantees.
/// </summary>
public class NotificationTemplateTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _typeId = Guid.NewGuid();

    private NotificationTemplate CreateTemplate(Guid? applicationId = null)
    {
        var result = NotificationTemplate.Create(
            _typeId, applicationId, NotificationChannelType.Email, "en", _userId);
        return result.Value;
    }

    private NotificationTemplate CreatePublishedTemplate(Guid? applicationId = null)
    {
        var template = CreateTemplate(applicationId);
        template.UpsertTranslation("en", "Subject EN", "<p>Body EN</p>", null, _userId);
        template.UpsertTranslation("ar", "Subject AR", "<p>Body AR</p>", null, _userId);
        template.Publish(_userId);
        template.ClearDomainEvents();
        return template;
    }

    #region Create

    [Fact]
    public void Create_ValidParameters_CreatesEmptyDraftVersionOne()
    {
        var template = CreateTemplate();

        template.DraftVersionId.Should().NotBeNull();
        template.PublishedVersionId.Should().BeNull();
        template.Versions.Should().HaveCount(1);
        template.DraftVersion!.VersionNumber.Should().Be(1);
        template.DraftVersion.Translations.Should().BeEmpty();
        template.DefaultLanguage.Should().Be("en");
    }

    [Fact]
    public void Create_UnsupportedDefaultLanguage_ReturnsError()
    {
        var result = NotificationTemplate.Create(
            _typeId, null, NotificationChannelType.Email, "xx", _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.UnsupportedLanguage");
    }

    [Fact]
    public void Create_CultureFormLanguage_NormalizesToTwoLetterCode()
    {
        var result = NotificationTemplate.Create(
            _typeId, null, NotificationChannelType.Email, "AR-sa", _userId);

        result.IsError.Should().BeFalse();
        result.Value.DefaultLanguage.Should().Be("ar");
    }

    #endregion

    #region Publish

    [Fact]
    public void Publish_WithoutDraft_ReturnsNoDraftError()
    {
        var template = CreatePublishedTemplate();

        var result = template.Publish(_userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.NoDraftToPublish");
    }

    [Fact]
    public void Publish_DraftMissingDefaultLanguageTranslation_ReturnsError()
    {
        var template = CreateTemplate();
        template.UpsertTranslation("ar", "Subject AR", "<p>Body AR</p>", null, _userId);

        var result = template.Publish(_userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.DefaultLanguageTranslationRequired");
        template.PublishedVersionId.Should().BeNull();
    }

    [Fact]
    public void Publish_ValidDraft_MovesPointerAndClearsDraftAndRaisesEvent()
    {
        var template = CreateTemplate();
        template.UpsertTranslation("en", "Subject EN", "<p>Body EN</p>", null, _userId);
        var draftId = template.DraftVersionId!.Value;

        var result = template.Publish(_userId);

        result.IsError.Should().BeFalse();
        template.PublishedVersionId.Should().Be(draftId);
        template.DraftVersionId.Should().BeNull();
        template.DomainEvents.Should().ContainSingle(e => e is NotificationTemplatePublishedEvent);
    }

    #endregion

    #region Draft lifecycle

    [Fact]
    public void EnsureDraft_AfterPublish_ClonesAllTranslationsIntoNextVersion()
    {
        var template = CreatePublishedTemplate();

        var draft = template.EnsureDraft(_userId);

        draft.VersionNumber.Should().Be(2);
        draft.Translations.Should().HaveCount(2);
        draft.Translations.Select(t => t.LanguageCode).Should().BeEquivalentTo(["en", "ar"]);
        template.DraftVersionId.Should().Be(draft.Id);
        // Publishing pointer untouched: edits never leak into the live version.
        template.PublishedVersion!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public void EnsureDraft_CalledTwice_ReturnsSameDraft()
    {
        var template = CreatePublishedTemplate();

        var first = template.EnsureDraft(_userId);
        var second = template.EnsureDraft(_userId);

        second.Id.Should().Be(first.Id);
        template.Versions.Should().HaveCount(2);
    }

    [Fact]
    public void UpsertTranslation_UnsupportedLanguage_ReturnsError()
    {
        var template = CreateTemplate();

        var result = template.UpsertTranslation("xx", "S", "<p>B</p>", null, _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.UnsupportedLanguage");
    }

    [Fact]
    public void RemoveTranslation_DefaultLanguage_ReturnsError()
    {
        var template = CreateTemplate();
        template.UpsertTranslation("en", "S", "<p>B</p>", null, _userId);

        var result = template.RemoveTranslation("en", _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.CannotRemoveDefaultLanguageTranslation");
    }

    [Fact]
    public void DiscardDraft_RemovesDraftVersionAndClearsPointer()
    {
        var template = CreatePublishedTemplate();
        template.EnsureDraft(_userId);

        var result = template.DiscardDraft(_userId);

        result.IsError.Should().BeFalse();
        template.DraftVersionId.Should().BeNull();
        template.Versions.Should().HaveCount(1);
        template.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void DiscardDraft_NoDraft_ReturnsError()
    {
        var template = CreatePublishedTemplate();

        var result = template.DiscardDraft(_userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.NoDraftToDiscard");
    }

    #endregion

    #region Unpublish

    [Fact]
    public void Unpublish_SystemGlobalTemplate_ReturnsForbiddenError()
    {
        var template = CreatePublishedTemplate(applicationId: null);

        var result = template.Unpublish(isSystemType: true, _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.CannotUnpublishSystemTemplate");
        template.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Unpublish_SystemTypeAppScopedOverride_Succeeds()
    {
        var template = CreatePublishedTemplate(applicationId: Guid.NewGuid());

        var result = template.Unpublish(isSystemType: true, _userId);

        result.IsError.Should().BeFalse();
        template.PublishedVersionId.Should().BeNull();
        template.DomainEvents.Should().ContainSingle(e => e is NotificationTemplateUnpublishedEvent);
    }

    [Fact]
    public void Unpublish_NotPublished_ReturnsError()
    {
        var template = CreateTemplate(applicationId: Guid.NewGuid());

        var result = template.Unpublish(isSystemType: false, _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.NotPublished");
    }

    #endregion

    #region Rollback

    [Fact]
    public void RollbackTo_UnknownVersion_ReturnsNotFoundError()
    {
        var template = CreatePublishedTemplate();

        var result = template.RollbackTo(Guid.NewGuid(), _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.VersionNotFound");
    }

    [Fact]
    public void RollbackTo_PreviousVersion_RepointsAndRaisesEvent()
    {
        var template = CreatePublishedTemplate();
        var v1Id = template.PublishedVersionId!.Value;

        // Create and publish version 2 with different content.
        template.UpsertTranslation("en", "Subject EN v2", "<p>Body EN v2</p>", null, _userId);
        template.Publish(_userId);
        template.ClearDomainEvents();
        var v2Id = template.PublishedVersionId!.Value;
        v2Id.Should().NotBe(v1Id);

        var result = template.RollbackTo(v1Id, _userId);

        result.IsError.Should().BeFalse();
        template.PublishedVersionId.Should().Be(v1Id);
        // All translations of v1 come back together — no cross-version mixing.
        template.PublishedVersion!.Translations.Select(t => t.Subject)
            .Should().BeEquivalentTo(["Subject EN", "Subject AR"]);
        template.DomainEvents.OfType<NotificationTemplateRolledBackEvent>().Should().ContainSingle()
            .Which.ToVersionId.Should().Be(v1Id);
    }

    [Fact]
    public void RollbackTo_CurrentDraft_ReturnsError()
    {
        var template = CreatePublishedTemplate();
        var draft = template.EnsureDraft(_userId);

        var result = template.RollbackTo(draft.Id, _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.NoDraftToPublish");
    }

    #endregion

    #region Restore draft from version

    [Fact]
    public void CreateDraftFromVersion_CopiesAllTranslations()
    {
        var template = CreatePublishedTemplate();
        var v1Id = template.PublishedVersionId!.Value;

        var result = template.CreateDraftFromVersion(v1Id, _userId);

        result.IsError.Should().BeFalse();
        result.Value.VersionNumber.Should().Be(2);
        result.Value.Translations.Should().HaveCount(2);
        template.DraftVersionId.Should().Be(result.Value.Id);
    }

    [Fact]
    public void CreateDraftFromVersion_DraftAlreadyPending_ReturnsError()
    {
        var template = CreatePublishedTemplate();
        var v1Id = template.PublishedVersionId!.Value;
        template.EnsureDraft(_userId);

        var result = template.CreateDraftFromVersion(v1Id, _userId);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.DraftAlreadyExists");
    }

    #endregion

    #region Deletion guard

    [Fact]
    public void EnsureDeletable_SystemGlobalTemplate_ReturnsForbiddenError()
    {
        var template = CreatePublishedTemplate(applicationId: null);

        var result = template.EnsureDeletable(isSystemType: true);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.CannotDeleteSystemGlobalTemplate");
    }

    [Fact]
    public void EnsureDeletable_SystemTypeAppScopedOverride_Succeeds()
    {
        var template = CreatePublishedTemplate(applicationId: Guid.NewGuid());

        var result = template.EnsureDeletable(isSystemType: true);

        result.IsError.Should().BeFalse();
    }

    #endregion
}
