using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for EnableTwoFactorCommandHandler.
/// </summary>
public class EnableTwoFactorCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<EnableTwoFactorCommandHandler>> _loggerMock;
    private readonly EnableTwoFactorCommandHandler _handler;

    public EnableTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _totpServiceMock = new Mock<ITotpService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<EnableTwoFactorCommandHandler>>();

        _handler = new EnableTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _totpServiceMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoTwoFactorSetup_ReturnsSetupRequiredError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.SetupRequired");
    }

    [Fact]
    public async Task Handle_TwoFactorAlreadyEnabled_ReturnsAlreadyEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: true);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorAlreadyEnabled");
    }

    [Fact]
    public async Task Handle_InvalidTotpCode_ReturnsInvalidCodeError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "000000");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");

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
    }

    [Fact]
    public async Task Handle_ValidCode_EnablesTwoFactorAndReturnsRecoveryCodes()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");
        var user = TestHelpers.CreateUser(id: userId);
        var recoveryCodes = new[] { "CODE1", "CODE2", "CODE3" };

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(true);

        _totpServiceMock
            .Setup(s => s.GenerateRecoveryCodes(10))
            .Returns(recoveryCodes);

        _totpServiceMock
            .Setup(s => s.HashRecoveryCode(It.IsAny<string>()))
            .Returns<string>(c => $"hashed_{c}");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().BeEquivalentTo(recoveryCodes);

        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCodeButUserNotFound_EnablesTwoFactorWithoutUserUpdate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");
        var recoveryCodes = new[] { "CODE1", "CODE2" };

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(true);

        _totpServiceMock
            .Setup(s => s.GenerateRecoveryCodes(10))
            .Returns(recoveryCodes);

        _totpServiceMock
            .Setup(s => s.HashRecoveryCode(It.IsAny<string>()))
            .Returns<string>(c => $"hashed_{c}");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().BeEquivalentTo(recoveryCodes);

        _userRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
