using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth_API.Modules.ApplicationManagement.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// OI-151: the create and update actions hand <c>postLogoutRedirectUris</c> to
/// their commands. The commands take it as an optional trailing argument, so an
/// action that stopped passing it would still compile and every handler test
/// would still pass; only this reaches the action.
/// </summary>
public class PostLogoutRedirectUrisControllerTests
{
    private const string Return = "https://shop.example.com/signed-out";

    private static ApplicationsController Controller(Mock<ISender> sender) =>
        new(sender.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task CreateApplication_PassesTheListToTheCommand()
    {
        var sender = new Mock<ISender>();
        CreateApplicationCommand? sent = null;
        sender
            .Setup(s => s.Send(It.IsAny<CreateApplicationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<ApplicationDto>>, CancellationToken>((command, _) => sent = (CreateApplicationCommand)command)
            .ReturnsAsync((ErrorOr<ApplicationDto>)new ApplicationDto { Id = Guid.NewGuid() });

        await Controller(sender).CreateApplication(
            new CreateApplicationRequest("SHOP", "Shop", PostLogoutRedirectUris: [Return]), CancellationToken.None);

        sent!.PostLogoutRedirectUris.Should().Equal(Return);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateApplication_PassesTheListOrItsAbsenceToTheCommand(bool withList)
    {
        var sender = new Mock<ISender>();
        UpdateApplicationCommand? sent = null;
        sender
            .Setup(s => s.Send(It.IsAny<UpdateApplicationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<ApplicationDto>>, CancellationToken>((command, _) => sent = (UpdateApplicationCommand)command)
            .ReturnsAsync((ErrorOr<ApplicationDto>)new ApplicationDto { Id = Guid.NewGuid() });

        await Controller(sender).UpdateApplication(
            Guid.NewGuid(),
            new UpdateApplicationRequest("Shop", PostLogoutRedirectUris: withList ? [Return] : null),
            CancellationToken.None);

        if (withList)
        {
            sent!.PostLogoutRedirectUris.Should().Equal(Return);
        }
        else
        {
            sent!.PostLogoutRedirectUris.Should().BeNull("null means unchanged, and must reach the handler as null");
        }
    }
}
