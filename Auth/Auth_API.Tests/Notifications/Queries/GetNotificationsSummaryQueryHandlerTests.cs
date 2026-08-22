using Auth.Application.Features.Notifications.GetNotificationsSummary;
using Auth.Domain.Enums;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;

namespace Auth_API.Tests.Notifications.Queries;

/// <summary>
/// Tests for the notifications section overview: the counts an operator reads
/// at a glance must reflect the aggregates behind them, and the "what is live"
/// list must show published templates only, newest first.
/// </summary>
public class GetNotificationsSummaryQueryHandlerTests
{
    private readonly Mock<INotificationTemplateRepository> _templateRepositoryMock = new();
    private readonly Mock<INotificationLayoutRepository> _layoutRepositoryMock = new();
    private readonly Mock<INotificationOutboxRepository> _outboxRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetNotificationsSummaryQueryHandler _handler;

    public GetNotificationsSummaryQueryHandlerTests()
    {
        _handler = new GetNotificationsSummaryQueryHandler(
            _templateRepositoryMock.Object,
            _layoutRepositoryMock.Object,
            _outboxRepositoryMock.Object,
            _applicationRepositoryMock.Object);
    }

    private static NotificationTemplateListItem Template(
        string typeCode,
        bool published,
        bool hasDraft = false,
        DateTime? modifiedAt = null)
    {
        return new NotificationTemplateListItem(
            Id: Guid.NewGuid(),
            NotificationTypeId: Guid.NewGuid(),
            TypeCode: typeCode,
            TypeName: typeCode,
            TypeIsSystem: true,
            ApplicationId: null,
            ApplicationName: null,
            Channel: (byte)NotificationChannelType.Email,
            DefaultLanguage: "en",
            PublishedVersionId: published ? Guid.NewGuid() : null,
            PublishedVersionNumber: published ? 2 : null,
            DraftVersionId: hasDraft ? Guid.NewGuid() : null,
            DraftVersionNumber: hasDraft ? 3 : null,
            TranslationCount: 7,
            CreatedAt: DateTime.UtcNow.AddDays(-30),
            ModifiedAt: modifiedAt);
    }

    private void Setup(
        IReadOnlyList<NotificationTemplateListItem> templates,
        IReadOnlyList<NotificationLayout>? layouts = null,
        NotificationOutboxStats? outbox = null)
    {
        _templateRepositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), null, null, null, null, null, null,
                It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((templates, templates.Count));

        _layoutRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(layouts ?? []);

        _outboxRepositoryMock
            .Setup(r => r.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(outbox ?? new NotificationOutboxStats(0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Handle_CountsTemplatesByPublicationState()
    {
        Setup([
            Template("password-reset", published: true),
            Template("email-verification", published: true, hasDraft: true),
            Template("ownership-transferred", published: false, hasDraft: true)
        ]);

        var result = await _handler.Handle(new GetNotificationsSummaryQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Templates.Total.Should().Be(3);
        result.Value.Templates.Published.Should().Be(2);
        result.Value.Templates.Drafts.Should().Be(2);
        result.Value.Templates.ByChannel.Should().ContainKey("Email").WhoseValue.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ListsPublishedTemplatesNewestFirst()
    {
        var older = Template("password-reset", published: true, modifiedAt: DateTime.UtcNow.AddDays(-2));
        var newer = Template("email-verification", published: true, modifiedAt: DateTime.UtcNow);
        Setup([older, newer, Template("draft-only", published: false)]);

        var result = await _handler.Handle(new GetNotificationsSummaryQuery(), CancellationToken.None);

        result.Value.PublishedTemplates.Should().HaveCount(2);
        result.Value.PublishedTemplates[0].TypeCode.Should().Be("email-verification");
        result.Value.PublishedTemplates[1].TypeCode.Should().Be("password-reset");
    }

    [Fact]
    public async Task Handle_CountsLayoutsByPublicationState()
    {
        var published = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Global", "<html></html>", "{}", Guid.NewGuid()).Value;
        published.Publish(published.ModifiedAt ?? published.CreatedAt, Guid.NewGuid());
        var draftOnly = NotificationLayout.Create(
            Guid.NewGuid(), NotificationChannelType.Email, "App", "<html></html>", "{}", Guid.NewGuid()).Value;

        Setup([], [published, draftOnly]);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Application?)null);

        var result = await _handler.Handle(new GetNotificationsSummaryQuery(), CancellationToken.None);

        result.Value.Layouts.Total.Should().Be(2);
        result.Value.Layouts.Published.Should().Be(1);
        result.Value.PublishedLayouts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_PassesThroughOutboxStats()
    {
        Setup([], outbox: new NotificationOutboxStats(
            Total: 120, Pending: 3, Sent: 110, Failed: 7, Last24Hours: 12));

        var result = await _handler.Handle(new GetNotificationsSummaryQuery(), CancellationToken.None);

        result.Value.Outbox.Total.Should().Be(120);
        result.Value.Outbox.Pending.Should().Be(3);
        result.Value.Outbox.Sent.Should().Be(110);
        result.Value.Outbox.Failed.Should().Be(7);
        result.Value.Outbox.Last24Hours.Should().Be(12);
    }
}
