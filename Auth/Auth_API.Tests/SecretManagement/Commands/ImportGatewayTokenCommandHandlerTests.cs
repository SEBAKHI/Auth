using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.ImportGatewayToken;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportGatewayTokenCommandHandler (bring-your-own-keys gateway token import).
/// </summary>
public class ImportGatewayTokenCommandHandlerTests
{
    private const string ValidToken = "a-sufficiently-long-gateway-token-value";

    private readonly SecretChallengeTestContext _challenges = new();
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<ImportGatewayTokenCommandHandler>> _loggerMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    private ImportGatewayTokenCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            _challenges.Service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _publisherMock.Object,
            _loggerMock.Object);

    /// <summary>Arranges a live approval bound to this exact token.</summary>
    private Guid ApprovalFor(string token)
    {
        var challengeId = Guid.NewGuid();
        _challenges.WithApproval(
            challengeId,
            SecretOperation.ImportGatewayToken,
            _admin,
            SecretPayloadDigest.Compute(token));
        return challengeId;
    }

    [Fact]
    public async Task Handle_ValidToken_Imports()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportGatewayTokenCommand(ValidToken, ApprovalFor(ValidToken), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(ValidToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflict()
    {
        // Arrange
        var handler = CreateHandler(storageMode: "PlainText");
        var command = new ImportGatewayTokenCommand(ValidToken, ApprovalFor(ValidToken), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ImportNotSupportedInPlainText", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithoutApproval_DoesNotImport()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new ImportGatewayTokenCommand(ValidToken, Guid.NewGuid(), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ChallengeNotApproved", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TokenSwappedAfterConfirmation_DoesNotImport()
    {
        // Arrange - the administrator confirmed one token and submitted another.
        var handler = CreateHandler();
        var command = new ImportGatewayTokenCommand(
            "a-completely-different-gateway-token", ApprovalFor(ValidToken), _admin);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ChallengeNotApproved", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
