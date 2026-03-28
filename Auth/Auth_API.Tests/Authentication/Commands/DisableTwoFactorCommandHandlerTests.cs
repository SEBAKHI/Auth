using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for DisableTwoFactorCommandHandler.
/// </summary>
public class DisableTwoFactorCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<DisableTwoFactorCommandHandler>> _loggerMock;
    private readonly DisableTwoFactorCommandHandler _handler;

    public DisableTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _totpServiceMock = new Mock<ITotpService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<DisableTwoFactorCommandHandler>>();

        _handler = new DisableTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _totpServiceMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoTwoFactorConfig_ReturnsTwoFactorNotEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
    }

    [Fact]
    public async Task Handle_TwoFactorNotEnabled_ReturnsTwoFactorNotEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
    }

    [Fact]
    public async Task Handle_TwoFactorLockedOut_ReturnsLockedOutError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            lockedUntil: DateTime.UtcNow.AddMinutes(10));

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");
    }

    [Fact]
    public async Task Handle_InvalidTotpCode_RecordsFailureAndReturnsInvalidCodeError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "000000");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "000000"))
            .Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");

        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCode_DisablesTwoFactorAndDispatchesEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");
        var user = TestHelpers.CreateUser(id: userId);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(true);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCodeButUserNotFound_DisablesTwoFactorWithoutUserUpdate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(true);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
