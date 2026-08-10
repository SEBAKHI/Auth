using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.ImportRsaKey;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportRsaKeyCommandHandler (bring-your-own-keys RSA import).
/// </summary>
public class ImportRsaKeyCommandHandlerTests
{
    private readonly SecretChallengeTestContext _challenges = new();
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<ImportRsaKeyCommandHandler>> _loggerMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    private ImportRsaKeyCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            _challenges.Service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _publisherMock.Object,
            _loggerMock.Object);

    private static string CreateRsaPrivateKeyPem(int keySizeBits = 2048)
    {
        using var rsa = RSA.Create(keySizeBits);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Arranges a live approval bound to this exact key material.</summary>
    private Guid ApprovalFor(string privateKeyPem)
    {
        var challengeId = Guid.NewGuid();
        _challenges.WithApproval(
            challengeId,
            SecretOperation.ImportRsaKey,
            _admin,
            SecretPayloadDigest.Compute(privateKeyPem));
        return challengeId;
    }

    [Fact]
    public async Task Handle_ValidKey_ImportsAndReturnsDerivedPublicKey()
    {
        // Arrange
        var privateKeyPem = CreateRsaPrivateKeyPem();
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _admin);

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
        var privateKeyPem = CreateRsaPrivateKeyPem();
        var handler = CreateHandler(storageMode: "PlainText");
        var command = new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _admin);

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
        var command = new ImportRsaKeyCommand("not-a-pem", ApprovalFor("not-a-pem"), _admin);

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
        var privateKeyPem = CreateRsaPrivateKeyPem(keySizeBits: 1024);
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _admin);

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
        var command = new ImportRsaKeyCommand(
            publicKeyOnlyPem, ApprovalFor(publicKeyOnlyPem), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.InvalidKeyMaterial", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WithoutApproval_DoesNotImport()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(CreateRsaPrivateKeyPem(), Guid.NewGuid(), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ChallengeNotApproved", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportRsaKeyPairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_KeyMaterialSwappedAfterConfirmation_DoesNotImport()
    {
        // Arrange - the administrator confirmed one key and submitted another.
        var confirmedKey = CreateRsaPrivateKeyPem();
        var swappedKey = CreateRsaPrivateKeyPem();
        var handler = CreateHandler();
        var command = new ImportRsaKeyCommand(swappedKey, ApprovalFor(confirmedKey), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ChallengeNotApproved", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportRsaKeyPairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
