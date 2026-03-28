using Auth.Application.Features.Users.ActivateAccount;
using Auth.Application.Features.Users.DeactivateAccount;
using Auth.Application.Features.Users.LockAccount;
using Auth.Application.Features.Users.UnlockAccount;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class ActivateAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly ActivateAccountCommandHandler _handler;

    public ActivateAccountCommandHandlerTests()
    {
        _handler = new ActivateAccountCommandHandler(
            _userRepositoryMock.Object,
            new Mock<ILogger<ActivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_ActivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Inactive);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new ActivateAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new ActivateAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class DeactivateAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly DeactivateAccountCommandHandler _handler;

    public DeactivateAccountCommandHandlerTests()
    {
        _handler = new DeactivateAccountCommandHandler(
            _userRepositoryMock.Object,
            _sessionRepositoryMock.Object,
            new Mock<ILogger<DeactivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_DeactivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Active);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new DeactivateAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new DeactivateAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class LockAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly LockAccountCommandHandler _handler;

    public LockAccountCommandHandlerTests()
    {
        _handler = new LockAccountCommandHandler(
            _userRepositoryMock.Object,
            _sessionRepositoryMock.Object,
            _eventDispatcherMock.Object,
            new Mock<ILogger<LockAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_LocksAccountAndTerminatesSessions()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new LockAccountCommand(userId, "Suspicious activity", 30, Guid.NewGuid());
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _sessionRepositoryMock.Verify(
            s => s.TerminateAllForUserAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(
            new LockAccountCommand(Guid.NewGuid(), "Test", null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class UnlockAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly UnlockAccountCommandHandler _handler;

    public UnlockAccountCommandHandlerTests()
    {
        _handler = new UnlockAccountCommandHandler(
            _userRepositoryMock.Object,
            _eventDispatcherMock.Object,
            new Mock<ILogger<UnlockAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_UnlocksAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Locked);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new UnlockAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new UnlockAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
