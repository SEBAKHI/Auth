using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Notifications.Persistence;

public class NotificationPublishRepositoryTests
{
    private readonly Guid _actorId = Guid.NewGuid();

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task LayoutTryPublish_UsesRevisionCompareAndSwap(int affectedRows, bool expected)
    {
        var factory = new RecordingDbConnectionFactory(affectedRows);
        var repository = new NotificationLayoutRepository(factory);
        var layout = NotificationLayout.Create(
            null,
            NotificationChannelType.Email,
            "Default",
            "<html>{{ content | raw }}</html>",
            "{}",
            _actorId).Value;
        var revision = layout.ModifiedAt ?? layout.CreatedAt;
        layout.Publish(revision, _actorId).IsError.Should().BeFalse();

        var result = await repository.TryPublishAsync(layout, revision, CancellationToken.None);

        result.Should().Be(expected);
        factory.LastCommand.Should().NotBeNull();
        factory.LastCommand!.CommandText.Should().Contain("[ModifiedAt] = @ExpectedRevisionAt");
        factory.LastCommand.Parameters["Id"].Should().Be(layout.Id);
        factory.LastCommand.Parameters["ExpectedRevisionAt"].Should().Be(revision);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task TemplateTryPublish_UsesDraftAndRevisionCompareAndSwap(
        int affectedRows,
        bool expected)
    {
        var factory = new RecordingDbConnectionFactory(affectedRows);
        var repository = new NotificationTemplateRepository(factory);
        var template = CreateTemplate();
        var draftId = template.DraftVersionId!.Value;
        var revision = template.ModifiedAt ?? template.CreatedAt;
        template.Publish(draftId, revision, _actorId).IsError.Should().BeFalse();

        var result = await repository.TryPublishAsync(
            template,
            draftId,
            revision,
            CancellationToken.None);

        result.Should().Be(expected);
        factory.LastCommand.Should().NotBeNull();
        factory.LastCommand!.CommandText.Should().Contain("[DraftVersionId] = @ExpectedDraftVersionId");
        factory.LastCommand.CommandText.Should().Contain("[ModifiedAt] = @ExpectedRevisionAt");
        factory.LastCommand.Parameters["ExpectedDraftVersionId"].Should().Be(draftId);
        factory.LastCommand.Parameters["ExpectedRevisionAt"].Should().Be(revision);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task TemplateTryUnpublish_UsesPublishedVersionCompareAndSwap(
        int affectedRows,
        bool expected)
    {
        var factory = new RecordingDbConnectionFactory(affectedRows);
        var repository = new NotificationTemplateRepository(factory);
        var template = CreateTemplate();
        var draftId = template.DraftVersionId!.Value;
        var revision = template.ModifiedAt ?? template.CreatedAt;
        template.Publish(draftId, revision, _actorId).IsError.Should().BeFalse();
        var publishedId = template.PublishedVersionId!.Value;
        template.Unpublish(false, publishedId, _actorId).IsError.Should().BeFalse();

        var result = await repository.TryUnpublishAsync(
            template,
            publishedId,
            CancellationToken.None);

        result.Should().Be(expected);
        factory.LastCommand.Should().NotBeNull();
        factory.LastCommand!.CommandText.Should().Contain(
            "[PublishedVersionId] = @ExpectedPublishedVersionId");
        factory.LastCommand.Parameters["ExpectedPublishedVersionId"].Should().Be(publishedId);
    }

    private NotificationTemplate CreateTemplate()
    {
        var template = NotificationTemplate.Create(
            Guid.NewGuid(),
            null,
            NotificationChannelType.Email,
            "en",
            _actorId).Value;
        template.UpsertTranslation(
            "en",
            "Subject",
            "<p>Hello</p>",
            null,
            _actorId).IsError.Should().BeFalse();
        return template;
    }
}
