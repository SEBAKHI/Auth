using System.Reflection;
using System.Security.Claims;
using Auth.Application.DTOs;
using Auth.Application.Features.Users.GetMyApplications;
using Auth.Sdk.Authorization;
using Auth_API.Modules.UserManagement.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.UserManagement;

/// <summary>
/// UI batch item 12: <c>GET /api/v1/Users/me/applications</c> answers for the signed-in caller
/// and nobody else. The user comes from the access token; the action takes no parameter that
/// could name another user, and needs no permission (it is the caller's own Home).
/// </summary>
public class MyApplicationsEndpointTests
{
    private static readonly MethodInfo Action =
        typeof(UsersController).GetMethod(nameof(UsersController.GetMyApplications))!;

    [Fact]
    public void TheAction_IsMeApplications_AndTakesNoUserParameter()
    {
        Action.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("me/applications");
        Action.GetParameters().Select(p => p.ParameterType).Should().Equal([typeof(CancellationToken)],
            "a user id, query string or body here would let one user read another's applications");
    }

    [Fact]
    public void TheAction_NeedsASignInButNoPermission()
    {
        typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        Action.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        Action.GetCustomAttributes<RequirePermissionAttribute>().Should().BeEmpty(
            "every signed-in person has a Home; a permission would leave most of them an empty page");
    }

    [Fact]
    public async Task TheAction_AsksForTheTokensUser()
    {
        var caller = Guid.NewGuid();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetMyApplicationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<IReadOnlyList<MyApplicationDto>>)new List<MyApplicationDto>());

        var result = await WithCaller(new UsersController(sender.Object), caller)
            .GetMyApplications(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        sender.Verify(s => s.Send(
            It.Is<GetMyApplicationsQuery>(q => q.UserId == caller),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheAction_WithoutAUserInTheToken_IsUnauthorizedAndAsksNothing()
    {
        var sender = new Mock<ISender>();

        var result = await WithCaller(new UsersController(sender.Object), caller: null)
            .GetMyApplications(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        sender.VerifyNoOtherCalls();
    }

    private static UsersController WithCaller(UsersController controller, Guid? caller)
    {
        List<Claim> claims = caller is null ? [] : [new Claim("sub", caller.Value.ToString())];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
        return controller;
    }
}
