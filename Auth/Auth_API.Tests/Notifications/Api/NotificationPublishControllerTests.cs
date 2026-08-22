using System.Security.Claims;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.PublishNotificationLayout;
using Auth.Application.Features.Notifications.PublishNotificationTemplate;
using Auth.Application.Features.Notifications.UnpublishNotificationTemplate;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth_API.Modules.NotificationManagement.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Notifications.Api;

public class NotificationPublishControllerTests
{
    private readonly Guid _actorId = Guid.NewGuid();

    [Fact]
    public async Task PublishLayout_MapsReviewedRevisionAndAuthenticatedActor()
    {
        var layoutId = Guid.NewGuid();
        var revision = DateTime.UtcNow;
        var dto = new NotificationLayoutDto { Id = layoutId };
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(
                It.IsAny<PublishNotificationLayoutCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<NotificationLayoutDto>)dto);
        var controller = WithActor(new NotificationLayoutsController(sender.Object));

        var result = await controller.Publish(
            layoutId,
            new PublishNotificationLayoutRequest(revision),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(dto);
        sender.Verify(s => s.Send(
            It.Is<PublishNotificationLayoutCommand>(command =>
                command.LayoutId == layoutId &&
                command.ExpectedRevisionAt == revision &&
                command.PublishedBy == _actorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishTemplate_MapsReviewedDraftRevisionAndAuthenticatedActor()
    {
        var templateId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var revision = DateTime.UtcNow;
        var dto = new NotificationTemplateDetailDto { Id = templateId };
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(
                It.IsAny<PublishNotificationTemplateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<NotificationTemplateDetailDto>)dto);
        var controller = WithActor(new NotificationTemplatesController(sender.Object));

        var result = await controller.Publish(
            templateId,
            new PublishNotificationTemplateRequest(draftId, revision),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(dto);
        sender.Verify(s => s.Send(
            It.Is<PublishNotificationTemplateCommand>(command =>
                command.TemplateId == templateId &&
                command.ExpectedDraftVersionId == draftId &&
                command.ExpectedRevisionAt == revision &&
                command.PublishedBy == _actorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnpublishTemplate_MapsReviewedLiveVersionAndAuthenticatedActor()
    {
        var templateId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        var dto = new NotificationTemplateDetailDto { Id = templateId };
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(
                It.IsAny<UnpublishNotificationTemplateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<NotificationTemplateDetailDto>)dto);
        var controller = WithActor(new NotificationTemplatesController(sender.Object));

        var result = await controller.Unpublish(
            templateId,
            new UnpublishNotificationTemplateRequest(publishedId),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(dto);
        sender.Verify(s => s.Send(
            It.Is<UnpublishNotificationTemplateCommand>(command =>
                command.TemplateId == templateId &&
                command.ExpectedPublishedVersionId == publishedId &&
                command.UnpublishedBy == _actorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private TController WithActor<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", _actorId.ToString())],
                    "test"))
            }
        };
        return controller;
    }
}
