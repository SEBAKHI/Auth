using Auth.Application.DTOs;
using Auth.Application.Features.Users.UpdateUser;

using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class UpdateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<UpdateUserCommandHandler>> _loggerMock;
    private readonly UpdateUserCommandHandler _handler;

    public UpdateUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<UpdateUserCommandHandler>>();

        _handler = new UpdateUserCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_ReturnsUpdatedUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new UpdateUserCommand(userId, "Updated", "User", "Updated User", null, "ar", "Asia/Riyadh")
        { ModifiedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(user, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateUserCommand(userId, "Updated", "User") { ModifiedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}
