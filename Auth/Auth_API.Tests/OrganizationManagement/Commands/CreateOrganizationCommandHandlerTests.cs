using Auth_API.Modules.OrganizationManagement.Commands;
using Auth_API.Tests.Helpers;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for CreateOrganizationCommandHandler.
/// </summary>
public class CreateOrganizationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILogger<CreateOrganizationCommandHandler>> _loggerMock;
    private readonly CreateOrganizationCommandHandler _handler;

    public CreateOrganizationCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<CreateOrganizationCommandHandler>>();

        _handler = new CreateOrganizationCommandHandler(
            _organizationRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _userRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidData_CreatesOrganizationSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new CreateOrganizationCommand(
            Code: "acme-corp",
            Name: "Acme Corporation",
            ContactEmail: "admin@acme.com",
            Description: "Test organization",
            LogoUrl: "https://acme.com/logo.png",
            Website: "https://acme.com")
        { CreatedBy = userId };

        var ownerRole = TestHelpers.CreateRole(
            id: roleId,
            code: "ORG-OWNER",
            name: "Organization Owner");

        var user = TestHelpers.CreateUser(
            id: userId,
            email: "creator@example.com",
            firstName: "John",
            lastName: "Doe");

        _organizationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRole);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _organizationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization org, CancellationToken _) => org);

        _organizationRepositoryMock
            .Setup(r => r.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser member, CancellationToken _) => member);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Code.Should().Be(command.Code);
        result.Value.Name.Should().Be(command.Name);
        result.Value.ContactEmail.Should().Be(command.ContactEmail);
        result.Value.OwnerId.Should().Be(userId);
        result.Value.OwnerName.Should().Be("John Doe");
        result.Value.OwnerEmail.Should().Be(user.Email);
        result.Value.IsActive.Should().BeTrue();
        result.Value.MemberCount.Should().Be(1);
        result.Value.EnabledAppCount.Should().Be(0);

        _organizationRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<Organization>(o =>
                o.Code == command.Code &&
                o.Name == command.Name &&
                o.OwnerId == userId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _organizationRepositoryMock.Verify(
            r => r.AddMemberAsync(It.Is<OrganizationUser>(m =>
                m.UserId == userId &&
                m.RoleId == roleId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateCode_ReturnsDuplicateCodeError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new CreateOrganizationCommand(
            Code: "existing-code",
            Name: "Test Organization",
            ContactEmail: "test@example.com")
        { CreatedBy = userId };

        _organizationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.DuplicateCode");

        _organizationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOwnerRoleNotFound_ReturnsUnexpectedError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new CreateOrganizationCommand(
            Code: "test-org",
            Name: "Test Organization",
            ContactEmail: "test@example.com")
        { CreatedBy = userId };

        _organizationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Unexpected);
        result.FirstError.Code.Should().Be("Organization.OwnerRoleNotFound");

        _organizationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithNullDescription_CreatesOrganizationSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new CreateOrganizationCommand(
            Code: "minimal-org",
            Name: "Minimal Organization",
            ContactEmail: "contact@minimal.com",
            Description: null,
            LogoUrl: null,
            Website: null)
        { CreatedBy = userId };

        var ownerRole = TestHelpers.CreateRole(
            id: roleId,
            code: "ORG-OWNER",
            name: "Organization Owner");

        _organizationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRole);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization org, CancellationToken _) => org);

        _organizationRepositoryMock
            .Setup(r => r.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser member, CancellationToken _) => member);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Code.Should().Be(command.Code);
        result.Value.Description.Should().BeNull();
        result.Value.LogoUrl.Should().BeNull();
        result.Value.Website.Should().BeNull();
        result.Value.OwnerName.Should().BeNull();
        result.Value.OwnerEmail.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PropagatesCancellationToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new CreateOrganizationCommand(
            Code: "test-org",
            Name: "Test Organization",
            ContactEmail: "test@example.com")
        { CreatedBy = userId };

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var ownerRole = TestHelpers.CreateRole(
            code: "ORG-OWNER",
            name: "Owner");

        _organizationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, cancellationToken))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", cancellationToken))
            .ReturnsAsync(ownerRole);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, cancellationToken))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Organization>(), cancellationToken))
            .ReturnsAsync((Organization org, CancellationToken _) => org);

        _organizationRepositoryMock
            .Setup(r => r.AddMemberAsync(It.IsAny<OrganizationUser>(), cancellationToken))
            .ReturnsAsync((OrganizationUser member, CancellationToken _) => member);

        // Act
        await _handler.Handle(command, cancellationToken);

        // Assert - verify all repository methods received the cancellation token
        _organizationRepositoryMock.Verify(
            r => r.ExistsByCodeAsync(command.Code, cancellationToken),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetByCodeAsync((Guid?)null, "org-owner", cancellationToken),
            Times.Once);
    }
}
