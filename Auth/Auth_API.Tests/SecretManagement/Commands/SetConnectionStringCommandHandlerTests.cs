using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.SetConnectionString;
using Auth.Application.Interfaces;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for SetConnectionStringCommandHandler.
/// </summary>
/// <remarks>
/// The behaviour under test is the asymmetry between the two ways a connection
/// string can be bad. Malformed text is refused outright — it can never start
/// working, and storing it would leave the API unable to boot with no way back in
/// through this endpoint. A parseable string that cannot reach the server is
/// refused once but storable on confirmation, because rotating the database
/// password has no other valid order.
/// </remarks>
public class SetConnectionStringCommandHandlerTests
{
    private const string Valid = "Server=localhost;Database=AuthDb;User Id=sa;Password=p;Encrypt=False";

    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IConnectionStringProbe> _probeMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<SetConnectionStringCommandHandler>> _loggerMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    public SetConnectionStringCommandHandlerTests()
    {
        WithProbe(isWellFormed: true, canConnect: true);
    }

    private void WithProbe(bool isWellFormed, bool canConnect, string? detail = null) =>
        _probeMock
            .Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionProbeResult(isWellFormed, canConnect, detail));

    private SetConnectionStringCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _secretServiceMock.Object,
            _probeMock.Object,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }),
            _publisherMock.Object,
            _loggerMock.Object);

    private void VerifyNothingStored() =>
        _secretServiceMock.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task Handle_ReachableConnectionString_WritesToTheAuthDbSlot()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: false, _admin), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync("ConnectionStrings.AuthDb", Valid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MalformedConnectionString_IsRefusedEvenWithForceSave()
    {
        // Arrange - unparseable text cannot become valid later, so no amount of
        // operator confirmation makes storing it reasonable.
        WithProbe(isWellFormed: false, canConnect: false, detail: "Keyword not supported: 'srv'.");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand("srv=nonsense", ForceSave: true, _admin), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ConnectionStringMalformed", result.FirstError.Code);
        VerifyNothingStored();
    }

    [Fact]
    public async Task Handle_UnreachableServerWithoutForceSave_IsRefused()
    {
        // Arrange
        WithProbe(isWellFormed: true, canConnect: false, detail: "Login failed for user 'sa'.");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: false, _admin), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.ConnectionStringUnreachable", result.FirstError.Code);
        VerifyNothingStored();
    }

    /// <summary>
    /// The password-rotation path. Refusing here would leave no valid order:
    /// changing the password at the server first takes the console down with the
    /// database, and storing the new string first fails a mandatory connect test
    /// because the credential is not live yet.
    /// </summary>
    [Fact]
    public async Task Handle_UnreachableServerWithForceSave_IsStored()
    {
        // Arrange
        WithProbe(isWellFormed: true, canConnect: false, detail: "Login failed for user 'sa'.");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: true, _admin), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync("ConnectionStrings.AuthDb", Valid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Success_PublishesAuditEventWithoutTheConnectionString()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: false, _admin), CancellationToken.None);

        // Assert - a connection string carries a credential, so only the key name
        // may reach the audit trail.
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SecretValueChangedEvent>(e =>
                    e.SecretKey == "ConnectionStrings.AuthDb"
                    && e.ChangedBy == _admin
                    && !e.SecretKey.Contains("Password=")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The audit handler writes its row through the database — the very thing
    /// this endpoint exists to repair when it is unreachable.
    /// </summary>
    /// <remarks>
    /// On the force-save rotation path the live credential is already dead, so
    /// the audit INSERT is guaranteed to throw. An unguarded publish would escape
    /// the handler (SqlException is neither SecretDecryptionException nor
    /// IOException) and return 500 for a write that had already landed on disk:
    /// the operator is told it failed, is never told a restart is required, and
    /// retrying reproduces the 500 while the file already holds the new value.
    /// </remarks>
    [Fact]
    public async Task Handle_AuditPublishThrows_StillReportsTheStoredValueAsSaved()
    {
        // Arrange - the database is unreachable, exactly as during a rotation.
        WithProbe(isWellFormed: true, canConnect: false, detail: "Login failed for user 'sa'.");
        _publisherMock
            .Setup(p => p.Publish(It.IsAny<SecretValueChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: true, _admin), CancellationToken.None);

        // Assert - the write happened, so the caller must be told it happened.
        Assert.False(result.IsError);
        _secretServiceMock.Verify(
            s => s.SetSecretAsync("ConnectionStrings.AuthDb", Valid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflictAndProbesNothing()
    {
        // Arrange
        var handler = CreateHandler(storageMode: "PlainText");

        // Act
        var result = await handler.Handle(
            new SetConnectionStringCommand(Valid, ForceSave: false, _admin), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Secret.SetNotSupportedInPlainText", result.FirstError.Code);
        _probeMock.Verify(
            p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNothingStored();
    }
}
