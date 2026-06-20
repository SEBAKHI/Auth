using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Application.Interfaces;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportHmacKeyCommandHandler (bring-your-own-keys HMAC import).
/// </summary>
public class ImportHmacKeyCommandHandlerTests
{
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<ILogger<ImportHmacKeyCommandHandler>> _loggerMock = new();

    private ImportHmacKeyCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _loggerMock.Object);

    private static string CreateHmacKeyBase64(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    [Fact]
    public async Task Handle_ValidKey_Imports()
    {
        // Arrange
        var keyBase64 = CreateHmacKeyBase64();
        var handler = CreateHandler();
        var command = new ImportHmacKeyCommand(keyBase64, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.ImportHmacKeyAsync(keyBase64, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflict()
    {
        // Arrange
        var handler = CreateHandler(storageMode: "PlainText");
        var command = new ImportHmacKeyCommand(CreateHmacKeyBase64(), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ImportNotSupportedInPlainText", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportHmacKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NotBase64_ReturnsInvalidKeyMaterial()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportHmacKeyCommand("!!!not-base64!!!", Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportHmacKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_KeyBelowMinimumLength_ReturnsInvalidKeyMaterial()
    {
        // Arrange - 16 bytes is below the 32-byte (256-bit) minimum.
        var handler = CreateHandler();
        var command = new ImportHmacKeyCommand(CreateHmacKeyBase64(bytes: 16), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
    }
}
