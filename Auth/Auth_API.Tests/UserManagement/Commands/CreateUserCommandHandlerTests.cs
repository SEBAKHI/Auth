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
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<CreateUserCommandHandler>> _loggerMock;
    private readonly PasswordValidator _passwordValidator;
    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<CreateUserCommandHandler>>();

        _passwordValidator = new PasswordValidator(
            TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings()));

        _handler = new CreateUserCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreatePassingReservationGuard(),
            _eventDispatcherMock.Object,
            new Mock<IPendingRegistrationConsumer>().Object,
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

    /// <summary>
    /// OI-109 T3: creating an account grants no role. Roles are given only through
    /// POST api/v1/users/{id}/roles, which runs both grant guards; a handler that
    /// cannot reach the role repository cannot write a role around them.
    /// </summary>
    [Fact]
    public void Constructor_DependsOnNoRoleRepository()
    {
        var dependencies = typeof(CreateUserCommandHandler).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        dependencies.Should().NotBeEmpty();
        dependencies.Should().NotContain(typeof(IRoleRepository),
            "a role written at creation skips PermissionGrantGuard and PlatformGrantFactorGuard");
    }

    [Fact]
    public async Task Handle_ValidData_ReturnsNoRolesAndWritesNone()
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
        result.Value.Roles.Should().BeEmpty();
        _userRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _userRepositoryMock.Verify(
            r => r.AssignRoleAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()),
            Times.Never());
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
