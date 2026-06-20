using Auth.Application.DTOs;
using Auth.Application.Features.Users.CreateUser;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class CreateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<CreateUserCommandHandler>> _loggerMock;
    private readonly PasswordValidator _passwordValidator;
    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<CreateUserCommandHandler>>();

        _passwordValidator = new PasswordValidator(
            TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings()));

        _handler = new CreateUserCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static CreateUserCommand CreateCommand(
        string email = "new@example.com",
        string password = "ValidPass1!",
        string firstName = "New",
        string lastName = "User") =>
        new(email, password, firstName, lastName) { CreatedBy = Guid.NewGuid() };

    [Fact]
    public async Task Handle_ValidData_ReturnsUserDto()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed");
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be(command.Email);
        result.Value.FirstName.Should().Be(command.FirstName);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ReturnsConflictError()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WeakPassword_ReturnsValidationError()
    {
        // Arrange
        var command = CreateCommand(password: "weak");
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithRoleIds_AssignsRoles()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(id: roleId, code: "ADMIN");
        var command = new CreateUserCommand(
            "new@example.com", "ValidPass1!", "New", "User",
            RoleIds: new List<Guid> { roleId })
        { CreatedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed");
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Roles.Should().Contain("ADMIN");
        _roleRepositoryMock.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ValidData_DispatchesDomainEvents()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed");
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
