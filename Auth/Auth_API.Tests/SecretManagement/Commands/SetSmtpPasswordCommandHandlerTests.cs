using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.SetSmtpPassword;
using Auth.Application.Interfaces;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for SetSmtpPasswordCommandHandler — storing the SMTP password in
/// the encrypted secrets file so it overrides <c>Email:Password</c>.
/// </summary>
public class SetSmtpPasswordCommandHandlerTests
{
    private const string Password = "an-smtp-password";

    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<SetSmtpPasswordCommandHandler>> _loggerMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    private SetSmtpPasswordCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _publisherMock.Object,
            _loggerMock.Object);

    /// <summary>
    /// The property name, not a "Custom:"-prefixed key: that prefix is what makes
    /// the generic custom-secret endpoint unusable here, because it lands the
    /// value under Secrets:Custom:* where nothing reads it.
    /// </summary>
    [Fact]
    public async Task Handle_ValidPassword_WritesToTheSmtpPasswordSlot()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SetSmtpPasswordCommand(Password, _admin), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync("SmtpPassword", Password, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Success_PublishesAuditEventNamingTheKeyOnly()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        await handler.Handle(new SetSmtpPasswordCommand(Password, _admin), CancellationToken.None);

        // Assert - the key name is audited; the value never is.
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SecretValueChangedEvent>(e =>
                    e.SecretKey == "SmtpPassword"
                    && e.ChangedBy == _admin
                    && !e.SecretKey.Contains(Password)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The secret is already on disk by the time the audit row is written, so a
    /// database outage must not turn a completed write into a reported failure.
    /// </summary>
    [Fact]
    public async Task Handle_AuditPublishThrows_StillReportsTheStoredValueAsSaved()
    {
        // Arrange
        _publisherMock
            .Setup(p => p.Publish(It.IsAny<SecretValueChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SetSmtpPasswordCommand(Password, _admin), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync("SmtpPassword", Password, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflictAndWritesNothing()
    {
        // Arrange - there is no encrypted file to write to, so storing would be a
        // no-op that reports success.
        var handler = CreateHandler(storageMode: "PlainText");

        // Act
        var result = await handler.Handle(new SetSmtpPasswordCommand(Password, _admin), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.SetNotSupportedInPlainText", result.FirstError.Code);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SecretFileUnreadable_ReturnsDecryptionFailed()
    {
        // Arrange
        _secretServiceMock
            .Setup(s => s.SetSecretAsync("SmtpPassword", Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecretDecryptionException("cannot decrypt"));
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SetSmtpPasswordCommand(Password, _admin), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.DecryptionFailed", result.FirstError.Code);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SecretValueChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
