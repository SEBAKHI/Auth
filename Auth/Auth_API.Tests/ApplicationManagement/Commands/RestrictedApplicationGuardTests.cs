using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Features.Organizations.EnableApplication;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Commands;

/// <summary>
/// Guards the invariant that a restricted application has no enabled
/// organizations. It admits only the users on its own access list, so an
/// organization can never enable it — held from both directions, because either
/// gap alone would leave the rule half-true and the access rule ambiguous.
/// </summary>
public class RestrictedApplicationGuardTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();

    private EnableApplicationCommandHandler CreateEnableHandler() => new(
        _organizationRepositoryMock.Object,
        _applicationRepositoryMock.Object,
        _userRepositoryMock.Object,
        new Mock<ILogger<EnableApplicationCommandHandler>>().Object);

    private UpdateApplicationCommandHandler CreateUpdateHandler() => new(
        _applicationRepositoryMock.Object,
        _refreshTokenRepositoryMock.Object,
        _sessionRepositoryMock.Object,
        ApplicationTestImages.Composer(),
        new Mock<ILogger<UpdateApplicationCommandHandler>>().Object);

    private void SetupOrganization()
    {
        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(_organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: _organizationId, isActive: true));
    }

    private ApplicationEntity SetupApplication(ApplicationAccessMode accessMode)
    {
        var application = TestHelpers.CreateApplication(code: "CRM", accessMode: accessMode);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        return application;
    }

    [Fact]
    public async Task EnableForOrganization_RestrictedApplication_IsRejected()
    {
        // Arrange
        SetupOrganization();
        var application = SetupApplication(ApplicationAccessMode.Restricted);

        // Act
        var result = await CreateEnableHandler().Handle(
            new EnableApplicationCommand(_organizationId, application.Id) { EnabledBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.RestrictedCannotBeEnabledForOrganization");
        _organizationRepositoryMock.Verify(
            r => r.EnableApplicationAsync(
                It.IsAny<Auth.Domain.Entities.OrganizationApplication>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnableForOrganization_OpenApplication_IsAllowed()
    {
        // Arrange
        SetupOrganization();
        var application = SetupApplication(ApplicationAccessMode.Everyone);

        // Act
        var result = await CreateEnableHandler().Handle(
            new EnableApplicationCommand(_organizationId, application.Id) { EnabledBy = _actorId },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Restricting_WhileOrganizationsAreEnabled_IsRejected()
    {
        // Arrange — refused rather than silently disabling them: several
        // companies losing access is a decision, not a side effect of changing
        // a dropdown.
        var application = SetupApplication(ApplicationAccessMode.Everyone);
        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await CreateUpdateHandler().Handle(
            new UpdateApplicationCommand(application.Id, "CRM", AccessMode: ApplicationAccessMode.Restricted)
            {
                ModifiedBy = _actorId
            },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        // Its own code, not the delete guard's: an administrator changing who
        // may sign in must not be told "cannot delete application".
        result.FirstError.Code.Should().Be("Application.CannotRestrictWithActiveOrganizations");
        application.AccessMode.Should().Be(ApplicationAccessMode.Everyone);
        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Restricting_WithNoOrganizations_SucceedsAndRevokesTheApplicationsTokens()
    {
        // Arrange
        var application = SetupApplication(ApplicationAccessMode.Everyone);
        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await CreateUpdateHandler().Handle(
            new UpdateApplicationCommand(application.Id, "CRM", AccessMode: ApplicationAccessMode.Restricted)
            {
                ModifiedBy = _actorId
            },
            CancellationToken.None);

        // Assert — everyone signed in did so under the open policy, and most of
        // them are no longer entitled.
        result.IsError.Should().BeFalse();
        application.AccessMode.Should().Be(ApplicationAccessMode.Restricted);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForApplicationAsync(
                application.Id, _actorId,
                TokenRevocationReasons.ApplicationAccessRevoked, It.IsAny<CancellationToken>()),
            Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateForApplicationAsync(
                application.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Opening_AnApplicationUp_RevokesNothing()
    {
        // Arrange — widening access takes nobody's away.
        var application = SetupApplication(ApplicationAccessMode.Restricted);

        // Act
        var result = await CreateUpdateHandler().Handle(
            new UpdateApplicationCommand(application.Id, "CRM", AccessMode: ApplicationAccessMode.Everyone)
            {
                ModifiedBy = _actorId
            },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        application.AccessMode.Should().Be(ApplicationAccessMode.Everyone);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
