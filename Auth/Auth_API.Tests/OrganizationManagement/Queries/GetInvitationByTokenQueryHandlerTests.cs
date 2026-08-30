using Auth.Application.Features.Organizations.GetInvitationByToken;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Interfaces;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Queries;

/// <summary>
/// Unit tests for GetInvitationByTokenQueryHandler.
/// </summary>
public class GetInvitationByTokenQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock = new();
    private readonly GetInvitationByTokenQueryHandler _handler;

    private const string Token = "cHJldmlldy10b2tlbi10aGF0LWlzLWxvbmctZW5vdWdo";

    public GetInvitationByTokenQueryHandlerTests()
    {
        // Identity hash - see the note in AcceptInvitationCommandHandlerTests.
        _tokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns<string>(token => token);

        _handler = new GetInvitationByTokenQueryHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _tokenKeyServiceMock.Object,
            new Mock<ILogger<GetInvitationByTokenQueryHandler>>().Object);
    }

    private (OrganizationInvitation invitation, Organization org, Role role, User inviter) SetupValidInvitation(
        InvitationStatus status = InvitationStatus.Pending,
        DateTime? expiresAt = null)
    {
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var invitation = TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: "invited@example.com",
            roleId: roleId,
            token: Token,
            status: status,
            expiresAt: expiresAt,
            invitedBy: inviterId);
        var org = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, firstName: "John", lastName: "Doe");

        _orgRepoMock.Setup(r => r.GetInvitationByTokenHashAsync(Token, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _userRepoMock.Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>())).ReturnsAsync(inviter);

        return (invitation, org, role, inviter);
    }

    [Fact]
    public async Task Handle_ValidToken_ReturnsPreviewWithUserExistsFalse()
    {
        SetupValidInvitation();
        _userRepoMock
            .Setup(r => r.ExistsByEmailAsync("invited@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(new GetInvitationByTokenQuery(Token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be("invited@example.com");
        result.Value.OrganizationName.Should().Be("Test Org");
        result.Value.RoleName.Should().Be("Member");
        result.Value.InvitedByName.Should().Be("John Doe");
        result.Value.Status.Should().Be("Pending");
        result.Value.IsExpired.Should().BeFalse();
        result.Value.UserExists.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserWithInvitedEmailExists_SetsUserExistsTrue()
    {
        SetupValidInvitation();
        _userRepoMock
            .Setup(r => r.ExistsByEmailAsync("invited@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(new GetInvitationByTokenQuery(Token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.UserExists.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsNotFound()
    {
        _orgRepoMock
            .Setup(r => r.GetInvitationByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation?)null);

        var result = await _handler.Handle(new GetInvitationByTokenQuery("garbage"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ReturnsPreviewWithIsExpiredTrue()
    {
        SetupValidInvitation(expiresAt: DateTime.UtcNow.AddDays(-1));

        var result = await _handler.Handle(new GetInvitationByTokenQuery(Token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AcceptedInvitation_ReturnsPreviewWithAcceptedStatus()
    {
        SetupValidInvitation(status: InvitationStatus.Accepted);

        var result = await _handler.Handle(new GetInvitationByTokenQuery(Token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be("Accepted");
    }
}
