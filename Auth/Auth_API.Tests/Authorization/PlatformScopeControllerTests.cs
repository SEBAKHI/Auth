using System.Security.Claims;
using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.DeleteOrganization;
using Auth.Application.Features.Organizations.GetOrganizationApplications;
using Auth.Application.Features.Organizations.GetOrganizationById;
using Auth.Application.Features.Organizations.GetOrganizationMembers;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Application.Features.Organizations.TransferOwnership;
using Auth.Application.Features.Users.GetUsers;
using Auth.Domain.Constants;
using Auth_API.Modules.OrganizationManagement.Contracts;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Auth_API.Modules.UserManagement.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// Seven places widen a request from "what this member may see" to "everything
/// on the platform", and each decides it from a permission claim rather than
/// from an endpoint gate.
/// </summary>
/// <remarks>
/// <para>
/// The attribute on these endpoints admits an ordinary organization member —
/// it has to, since members legitimately call them. What separates a member's
/// answer from an operator's is a single boolean read off the token inside the
/// action, and until now not one of those seven reads was covered by a test.
/// An inverted condition, or a widening keyed on the wrong code, would have
/// handed every organization on the platform to any member who asked, and the
/// suite would have stayed green.
/// </para>
/// <para>
/// Both directions are asserted for each site. Only checking the granted case
/// would pass a condition hardwired to true, which is the exact shape of the
/// failure being guarded against.
/// </para>
/// </remarks>
public class PlatformScopeControllerTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetOrganization_WidensOnlyForTheOrganizationsReadClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetOrganizationByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<OrganizationDetailDto>)new OrganizationDetailDto());

        await Organizations(sender, held ? PermissionCodes.Organizations.Read : null)
            .GetOrganization(OrgId, CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<GetOrganizationByIdQuery>(q => q.PlatformScope == held && q.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteOrganization_WidensOnlyForTheOrganizationsManageClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<DeleteOrganizationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Deleted>)Result.Deleted);

        await Organizations(sender, held ? PermissionCodes.Organizations.Manage : null)
            .DeleteOrganization(OrgId, CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<DeleteOrganizationCommand>(c => c.PlatformScope == held && c.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransferOwnership_WidensOnlyForTheOrganizationsManageClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<TransferOwnershipCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);

        await Organizations(sender, held ? PermissionCodes.Organizations.Manage : null)
            .TransferOwnership(
                OrgId,
                new TransferOwnershipRequest(Guid.NewGuid(), "000000"),
                CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<TransferOwnershipCommand>(c => c.PlatformScope == held && c.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetMembers_WidensOnlyForTheOrganizationsReadClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetOrganizationMembersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PagedOrganizationMembersDto>)new PagedOrganizationMembersDto());

        await Organizations(sender, held ? PermissionCodes.Organizations.Read : null)
            .GetMembers(OrgId, cancellationToken: CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<GetOrganizationMembersQuery>(q => q.PlatformScope == held && q.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPendingInvitations_WidensOnlyForTheOrganizationsReadClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPendingInvitationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<IReadOnlyList<OrganizationInvitationDto>>)new List<OrganizationInvitationDto>());

        await Organizations(sender, held ? PermissionCodes.Organizations.Read : null)
            .GetPendingInvitations(OrgId, cancellationToken: CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<GetPendingInvitationsQuery>(q => q.PlatformScope == held && q.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetApplications_WidensOnlyForTheOrganizationsReadClaim(bool held)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetOrganizationApplicationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<IReadOnlyList<OrganizationApplicationDto>>)new List<OrganizationApplicationDto>());

        await Organizations(sender, held ? PermissionCodes.Organizations.Read : null)
            .GetApplications(OrgId, cancellationToken: CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<GetOrganizationApplicationsQuery>(q => q.PlatformScope == held && q.RequestedBy == Actor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUsers_RefusesDeletedRows_WithoutTheUsersManageClaim()
    {
        var sender = new Mock<ISender>();

        var result = await Users(sender, permission: null)
            .GetUsers(includeDeleted: true, cancellationToken: CancellationToken.None);

        // Refused before the query is built, so the handler never sees the ask.
        result.Should().BeOfType<ObjectResult>();
        sender.Verify(s => s.Send(It.IsAny<GetUsersQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetUsers_PassesIncludeDeletedThrough_WithTheUsersManageClaim(bool includeDeleted)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PagedUsersDto>)new PagedUsersDto());

        await Users(sender, PermissionCodes.Users.Manage)
            .GetUsers(includeDeleted: includeDeleted, cancellationToken: CancellationToken.None);

        sender.Verify(s => s.Send(
            It.Is<GetUsersQuery>(q => q.IncludeDeleted == includeDeleted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OrganizationsController Organizations(Mock<ISender> sender, string? permission) =>
        WithCaller(new OrganizationsController(sender.Object), permission);

    private static UsersController Users(Mock<ISender> sender, string? permission) =>
        WithCaller(new UsersController(sender.Object), permission);

    /// <summary>A signed-in caller holding at most one permission claim.</summary>
    private static TController WithCaller<TController>(TController controller, string? permission)
        where TController : ControllerBase
    {
        List<Claim> claims = [new Claim("sub", Actor.ToString())];
        if (permission is not null)
        {
            claims.Add(new Claim(JwtClaimNames.Permissions, permission));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                // Empty on purpose. ApiController.Problem resolves its
                // localizers with GetService and copes with their absence, but
                // it dereferences RequestServices itself.
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };

        return controller;
    }
}
