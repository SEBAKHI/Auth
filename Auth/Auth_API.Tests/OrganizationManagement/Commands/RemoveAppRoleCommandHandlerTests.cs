using Auth.Application.Features.Organizations.RemoveAppRole;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for RemoveAppRoleCommandHandler.
/// </summary>
public class RemoveAppRoleCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<RemoveAppRoleCommandHandler>> _loggerMock;
    private readonly RemoveAppRoleCommandHandler _handler;

    public RemoveAppRoleCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _loggerMock = new Mock<ILogger<RemoveAppRoleCommandHandler>>();

        _handler = new RemoveAppRoleCommandHandler(
            _organizationRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _publisherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var command = new RemoveAppRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        {
            RemovedBy = Guid.NewGuid()
        };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.NotFound");
    }

    [Fact]
    public async Task Handle_WhenUserNotMember_ReturnsNotMemberError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new RemoveAppRoleCommand(orgId, userId, Guid.NewGuid()) { RemovedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.NotMember");
    }

    [Fact]
    public async Task Handle_WhenRoleNotFound_ReturnsRoleNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new RemoveAppRoleCommand(orgId, userId, roleId) { RemovedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.RoleNotFound");
    }

    [Fact]
    public async Task Handle_WhenRoleIsPlatformRole_ReturnsNotAssignedError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new RemoveAppRoleCommand(orgId, userId, roleId) { RemovedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: null, code: "PLATFORM-ADMIN"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.AppRoleNotAssigned");
    }

    [Fact]
    public async Task Handle_WhenRoleNotAssigned_ReturnsNotAssignedError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new RemoveAppRoleCommand(orgId, userId, roleId) { RemovedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: appId));

        _organizationRepositoryMock
            .Setup(r => r.RemoveAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: no row removed (absent, or a concurrent removal won) is a 404, unaudited.
        // Deliberate break: ignore the row count.
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.AppRoleNotAssigned");
        _publisherMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAssignedRole_RemovesAssignment()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new RemoveAppRoleCommand(orgId, userId, roleId) { RemovedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: appId));

        _organizationRepositoryMock
            .Setup(r => r.RemoveAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: removed by its full key whatever its state (no validity pre-check that
        // an expired row fails), and recorded.
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(Result.Deleted);
        _organizationRepositoryMock.Verify(
            r => r.RemoveAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>()),
            Times.Once);
        _organizationRepositoryMock.Verify(
            r => r.HasAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<OrganizationAppRoleRemovedEvent>(e =>
                    e.OrganizationId == orgId && e.UserId == userId && e.ApplicationId == appId
                    && e.RoleId == roleId && e.RemovedBy == command.RemovedBy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
