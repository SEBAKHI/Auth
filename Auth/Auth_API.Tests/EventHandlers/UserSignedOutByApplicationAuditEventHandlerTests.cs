using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// The audit row of a server-side application's sign-out (OI-183): a sign-out of one device, with
/// the application, the session, and whether the browser's single sign-on session ended.
/// </summary>
public class UserSignedOutByApplicationAuditEventHandlerTests
{
    private static UserSignedOutByApplicationEvent ASignOut(bool singleSignOnEnded = true)
    {
        var userId = Guid.NewGuid();
        return new UserSignedOutByApplicationEvent(
            Guid.NewGuid(), userId, Guid.NewGuid(), Guid.NewGuid(), singleSignOnEnded, userId, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WritesASignOutOfOneDevice_WithTheApplicationTheSessionAndTheSingleSignOnFlag(bool singleSignOnEnded)
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var signOut = ASignOut(singleSignOnEnded);

        await new UserSignedOutByApplicationAuditEventHandler(audit.Object, NullLogger<UserSignedOutByApplicationAuditEventHandler>.Instance)
            .Handle(signOut, CancellationToken.None);

        written!.Action.Should().Be(AuditActions.UserLogout);
        written.ActionType.Should().Be(AuditActionTypes.Authentication);
        written.UserId.Should().Be(signOut.UserId);
        written.PerformedBy.Should().Be(signOut.TriggeredBy);
        written.ApplicationId.Should().Be(signOut.ApplicationId);
        written.SessionId.Should().Be(signOut.SessionId);
        using var details = JsonDocument.Parse(written.AdditionalData!);
        details.RootElement.GetProperty("signedOutBy").GetString().Should().Be("application");
        details.RootElement.GetProperty("singleSignOnEnded").GetBoolean().Should().Be(singleSignOnEnded);
    }

    [Fact]
    public async Task Handle_WhenTheRowCannotBeWritten_LogsAndNeverFailsTheSignOut()
    {
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var logger = new Mock<ILogger<UserSignedOutByApplicationAuditEventHandler>>();

        var handle = () => new UserSignedOutByApplicationAuditEventHandler(audit.Object, logger.Object)
            .Handle(ASignOut(), CancellationToken.None);

        await handle.Should().NotThrowAsync("the sessions already ended: the answer must still reach the application");
        logger.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
