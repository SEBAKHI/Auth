using Auth.Application.Features.Users.DeleteUser;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class DeleteUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ILogger<DeleteUserCommandHandler>> _loggerMock;
    private readonly DeleteUserCommandHandler _handler;

    public DeleteUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<DeleteUserCommandHandler>>();
        _handler = new DeleteUserCommandHandler(
            _userRepositoryMock.Object, _publisherMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidUser_DeletesAndPublishesEvent()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        var command = new DeleteUserCommand(Guid.NewGuid()) { DeletedBy = Guid.NewGuid() };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_SystemUser_ReturnsForbiddenError()
    {
        var userId = Guid.NewGuid();
        // Create a system user via constructor - need to check if there's a way
        // System user has IsSystemUser = true
        var user = new User(
            id: userId, email: "system@test.com", normalizedEmail: "SYSTEM@TEST.COM",
            passwordHash: "hash", firstName: "System", lastName: "User",
            displayName: null, phoneNumber: null,
            status: Auth.Domain.Enums.UserStatus.Active,
            emailConfirmed: true, phoneConfirmed: false,
            twoFactorEnabled: false, twoFactorSecret: null,
            failedLoginAttempts: 0, lockoutEnd: null, lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow, mustChangePassword: false,
            preferredLanguage: "en", timeZone: "UTC", metadata: null,
            isSystemUser: true,
            createdAt: DateTime.UtcNow, createdBy: Guid.NewGuid(),
            modifiedAt: null, modifiedBy: null);

        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
    }
}
