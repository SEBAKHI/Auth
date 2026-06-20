using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.ImportGatewayToken;
using Auth.Application.Interfaces;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportGatewayTokenCommandHandler (bring-your-own-keys gateway token import).
/// </summary>
public class ImportGatewayTokenCommandHandlerTests
{
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<ILogger<ImportGatewayTokenCommandHandler>> _loggerMock = new();

    private ImportGatewayTokenCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _loggerMock.Object);

    [Fact]
    public async Task Handle_ValidToken_Imports()
    {
        // Arrange
        const string token = "a-sufficiently-long-gateway-token-value";
        var handler = CreateHandler();
        var command = new ImportGatewayTokenCommand(token, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(token, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflict()
    {
        // Arrange
        var handler = CreateHandler(storageMode: "PlainText");
        var command = new ImportGatewayTokenCommand("a-sufficiently-long-gateway-token-value", Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ImportNotSupportedInPlainText", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.ImportGatewayTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
