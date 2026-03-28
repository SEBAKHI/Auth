using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for SetupTwoFactorCommandHandler.
/// </summary>
public class SetupTwoFactorCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<ILogger<SetupTwoFactorCommandHandler>> _loggerMock;
    private readonly SetupTwoFactorCommandHandler _handler;

    public SetupTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _totpServiceMock = new Mock<ITotpService>();
        _loggerMock = new Mock<ILogger<SetupTwoFactorCommandHandler>>();

        var jwtSettings = Options.Create(new JwtSettings
        {
            Issuer = "TestIssuer"
        });

        _handler = new SetupTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _totpServiceMock.Object,
            jwtSettings,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_TwoFactorAlreadyEnabled_ReturnsConflictError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId);
        var user = TestHelpers.CreateUser(id: userId);
        var existingTwoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: true);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTwoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorAlreadyEnabled");
    }

    [Fact]
    public async Task Handle_NoPriorSetup_CreatesNewTwoFactorAndReturnsSetupResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId);
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        var secret = "ABCDEFGHIJKLMNOP";
        var qrCodeUri = "otpauth://totp/TestIssuer:test@example.com?secret=ABCDEFGHIJKLMNOP&issuer=TestIssuer";

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        _totpServiceMock
            .Setup(s => s.GenerateSecret())
            .Returns(secret);

        _totpServiceMock
            .Setup(s => s.GenerateQrCodeUri(secret, user.Email, "TestIssuer"))
            .Returns(qrCodeUri);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Secret.Should().Be(secret);
        result.Value.QrCodeUri.Should().Be(qrCodeUri);
        result.Value.ManualEntryKey.Should().Be("ABCD EFGH IJKL MNOP");

        _twoFactorRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Auth.Domain.Entities.TwoFactorAuth>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingDisabledSetup_DeletesOldAndCreatesNew()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId);
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        var existingTwoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false);
        var secret = "NEWBASE32SECRET1";
        var qrCodeUri = "otpauth://totp/TestIssuer:test@example.com?secret=NEWBASE32SECRET1&issuer=TestIssuer";

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTwoFactor);

        _totpServiceMock
            .Setup(s => s.GenerateSecret())
            .Returns(secret);

        _totpServiceMock
            .Setup(s => s.GenerateQrCodeUri(secret, user.Email, "TestIssuer"))
            .Returns(qrCodeUri);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Secret.Should().Be(secret);

        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _twoFactorRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Auth.Domain.Entities.TwoFactorAuth>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NullIssuer_UsesDefaultIssuer()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId);
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        var secret = "ABCDEFGHIJKLMNOP";

        var jwtSettingsNoIssuer = Options.Create(new JwtSettings { Issuer = null! });
        var handler = new SetupTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _totpServiceMock.Object,
            jwtSettingsNoIssuer,
            _loggerMock.Object);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        _totpServiceMock
            .Setup(s => s.GenerateSecret())
            .Returns(secret);

        _totpServiceMock
            .Setup(s => s.GenerateQrCodeUri(secret, user.Email, "AuthSystem"))
            .Returns("otpauth://totp/AuthSystem:test@example.com");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _totpServiceMock.Verify(
            s => s.GenerateQrCodeUri(secret, user.Email, "AuthSystem"),
            Times.Once);
    }
}
