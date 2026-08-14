using Auth.Application.Features.Applications.GrantApplicationAccess;
using Auth.Application.Features.Applications.RevokeApplicationAccess;
using Auth.Application.Features.Applications.SetApplicationActive;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Commands;

/// <summary>
/// Unit tests for the application access list (invite / withdraw) and the
/// on/off switch.
/// </summary>
public class ApplicationAccessCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IApplicationAccessRepository> _accessRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();

    private readonly ApplicationEntity _application = TestHelpers.CreateApplication(code: "CRM");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();

    private GrantApplicationAccessCommandHandler CreateGrantHandler() => new(
        _applicationRepositoryMock.Object,
        _accessRepositoryMock.Object,
        _userRepositoryMock.Object,
        _roleRepositoryMock.Object,
        _publisherMock.Object,
        new Mock<ILogger<GrantApplicationAccessCommandHandler>>().Object);

    private RevokeApplicationAccessCommandHandler CreateRevokeHandler() => new(
        _applicationRepositoryMock.Object,
        _accessRepositoryMock.Object,
        _refreshTokenRepositoryMock.Object,
        _sessionRepositoryMock.Object,
        _publisherMock.Object,
        new Mock<ILogger<RevokeApplicationAccessCommandHandler>>().Object);

    private SetApplicationActiveCommandHandler CreateActiveHandler() => new(
        _applicationRepositoryMock.Object,
        _refreshTokenRepositoryMock.Object,
        _sessionRepositoryMock.Object,
        _publisherMock.Object,
        new Mock<ILogger<SetApplicationActiveCommandHandler>>().Object);

    private void SetupApplicationAndUser()
    {
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId));
    }

    #region Grant

    [Fact]
    public async Task Grant_NewInvitation_CreatesGrantAndPublishesEvent()
    {
        // Arrange
        SetupApplicationAndUser();

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, _userId) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(
                It.Is<ApplicationUserAccess>(g => g.UserId == _userId && g.ApplicationId == _application.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<ApplicationAccessGrantedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Grant_PreviouslyRevoked_ReinstatesTheSameRow()
    {
        // Arrange — the unique (application, user) constraint means the earlier
        // row must be revived, not duplicated; the past trial stays on record.
        SetupApplicationAndUser();

        var revoked = ApplicationUserAccess.Create(_application.Id, _userId, _actorId);
        revoked.Revoke(_actorId);

        _accessRepositoryMock
            .Setup(r => r.GetGrantAsync(_application.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revoked);

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, _userId) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        revoked.IsValid().Should().BeTrue();
        _accessRepositoryMock.Verify(
            r => r.UpdateGrantAsync(revoked, It.IsAny<CancellationToken>()), Times.Once);
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Grant_AlreadyInvited_ReturnsConflict()
    {
        // Arrange
        SetupApplicationAndUser();
        _accessRepositoryMock
            .Setup(r => r.GetGrantAsync(_application.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationUserAccess.Create(_application.Id, _userId, _actorId));

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, _userId) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.UserAccessAlreadyGranted");
    }

    [Fact]
    public async Task Grant_ApplicationNotFound_ReturnsNotFound()
    {
        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(Guid.NewGuid(), _userId) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.NotFound");
    }

    [Fact]
    public async Task Grant_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, Guid.NewGuid()) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Grant_WithRole_AssignsItScopedToTheApplication()
    {
        // Arrange — the invitation opens the door; the role is what the invitee
        // can then do, and it must not travel to other applications.
        SetupApplicationAndUser();
        var role = TestHelpers.CreateRole(applicationId: _application.Id, name: "Trial User");
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, _userId, RoleId: role.Id) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _roleRepositoryMock.Verify(
            r => r.AssignToUserAsync(
                It.Is<UserRole>(ur => ur.ApplicationId == _application.Id && ur.RoleId == role.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Grant_WithRoleOwnedByAnotherApplication_IsRejected()
    {
        // Arrange
        SetupApplicationAndUser();
        var foreignRole = TestHelpers.CreateRole(applicationId: Guid.NewGuid(), name: "Other App Admin");
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(foreignRole.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreignRole);

        // Act
        var result = await CreateGrantHandler().Handle(
            new GrantApplicationAccessCommand(_application.Id, _userId, RoleId: foreignRole.Id) { GrantedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Revoke

    [Fact]
    public async Task Revoke_ActiveInvitation_RevokesRowTokensAndSessions()
    {
        // Arrange
        var grant = ApplicationUserAccess.Create(_application.Id, _userId, _actorId);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);
        _accessRepositoryMock
            .Setup(r => r.GetGrantAsync(_application.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        // Act
        var result = await CreateRevokeHandler().Handle(
            new RevokeApplicationAccessCommand(_application.Id, _userId) { RevokedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        grant.IsValid().Should().BeFalse();
        grant.RevokedBy.Should().Be(_actorId);

        // This user, this application. Losing one application must not sign the
        // user out of the others.
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeForUserAndApplicationAsync(
                _userId, _application.Id, _actorId,
                TokenRevocationReasons.ApplicationAccessRevoked, It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sessionRepositoryMock.Verify(
            r => r.TerminateForUserAndApplicationAsync(
                _userId, _application.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<ApplicationAccessRevokedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Revoke_NoActiveInvitation_ReturnsNotFound()
    {
        // Arrange
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);

        // Act
        var result = await CreateRevokeHandler().Handle(
            new RevokeApplicationAccessCommand(_application.Id, _userId) { RevokedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.UserAccessNotFound");
    }

    #endregion

    #region Activate / deactivate

    [Fact]
    public async Task Deactivate_RevokesEveryTokenAndSessionForTheApplication()
    {
        // Arrange
        var application = TestHelpers.CreateApplication(code: "CRM", isActive: true);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await CreateActiveHandler().Handle(
            new SetApplicationActiveCommand(application.Id, false) { ModifiedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        application.IsActive.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForApplicationAsync(
                application.Id, _actorId,
                TokenRevocationReasons.ApplicationDeactivated, It.IsAny<CancellationToken>()),
            Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateForApplicationAsync(
                application.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Deactivate_LeavesTheAccessModeAlone()
    {
        // Arrange — the two switches are independent: an application switched
        // back on must return with the audience it had.
        var application = TestHelpers.CreateApplication(
            code: "CRM", isActive: true, accessMode: Auth.Domain.Enums.ApplicationAccessMode.Restricted);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        await CreateActiveHandler().Handle(
            new SetApplicationActiveCommand(application.Id, false) { ModifiedBy = _actorId },
            CancellationToken.None);

        // Assert
        application.AccessMode.Should().Be(Auth.Domain.Enums.ApplicationAccessMode.Restricted);
    }

    [Fact]
    public async Task Activate_RevokesNothing()
    {
        // Arrange
        var application = TestHelpers.CreateApplication(code: "CRM", isActive: false);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await CreateActiveHandler().Handle(
            new SetApplicationActiveCommand(application.Id, true) { ModifiedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        application.IsActive.Should().BeTrue();
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetActive_NoChange_SucceedsWithoutRevoking()
    {
        // Arrange — a double-click on the switch is not an error.
        var application = TestHelpers.CreateApplication(code: "CRM", isActive: true);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await CreateActiveHandler().Handle(
            new SetApplicationActiveCommand(application.Id, true) { ModifiedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetActive_ApplicationNotFound_ReturnsNotFound()
    {
        // Act
        var result = await CreateActiveHandler().Handle(
            new SetApplicationActiveCommand(Guid.NewGuid(), false) { ModifiedBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.NotFound");
    }

    #endregion
}
