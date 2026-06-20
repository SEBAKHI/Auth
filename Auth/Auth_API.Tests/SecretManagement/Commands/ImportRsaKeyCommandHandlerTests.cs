using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.ImportRsaKey;
using Auth.Application.Interfaces;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportRsaKeyCommandHandler (bring-your-own-keys RSA import).
/// </summary>
public class ImportRsaKeyCommandHandlerTests
{
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<ILogger<ImportRsaKeyCommandHandler>> _loggerMock = new();

    private ImportRsaKeyCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _loggerMock.Object);

    private static string CreateRsaPrivateKeyPem(int keySizeBits = 2048)
    {
        using var rsa = RSA.Create(keySizeBits);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    [Fact]
    public async Task Handle_ValidKey_ImportsAndReturnsDerivedPublicKey()
    {
        // Arrange
        var privateKeyPem = CreateRsaPrivateKeyPem();
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(privateKeyPem, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        Assert.Contains("BEGIN PUBLIC KEY", result.Value);
        _secretServiceMock.Verify(
            s => s.ImportRsaKeyPairAsync(privateKeyPem, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflictAndDoesNotImport()
    {
        // Arrange
        var handler = CreateHandler(storageMode: "PlainText");
        var command = new ImportRsaKeyCommand(CreateRsaPrivateKeyPem(), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ImportNotSupportedInPlainText", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportRsaKeyPairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NotAPem_ReturnsInvalidKeyMaterial()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand("not-a-pem", Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportRsaKeyPairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_KeyBelowMinimumSize_ReturnsInvalidKeyMaterial()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(CreateRsaPrivateKeyPem(keySizeBits: 1024), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PublicKeyOnly_ReturnsInvalidKeyMaterial()
    {
        // Arrange - supplying a public key PEM (no private component) must be rejected.
        using var rsa = RSA.Create(2048);
        var publicKeyOnlyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(publicKeyOnlyPem, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
    }
}
