using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// OI-155 A6: one audit row per single logout that ended something, and a failure to write it
/// never reaches the sign-out that published it.
/// </summary>
public class SingleLogoutCompletedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogs = new();

    private SingleLogoutCompletedAuditEventHandler Handler() =>
        new(_auditLogs.Object, NullLogger<SingleLogoutCompletedAuditEventHandler>.Instance);

    private static SingleLogoutCompletedEvent Event(Guid userId, Guid idpSessionId, Guid? triggeredBy) => new(
        Guid.NewGuid(), userId, idpSessionId, EndedSignInCount: 3, SingleLogoutTriggers.OtherUserSignedIn,
        triggeredBy, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_WritesOneSignOutRow_WithTheTriggerTheSessionAndTheCount()
    {
        var userId = Guid.NewGuid();
        var idpSessionId = Guid.NewGuid();
        var nextPerson = Guid.NewGuid();
        AuditLogEntity? written = null;
        _auditLogs
            .Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);

        await Handler().Handle(Event(userId, idpSessionId, nextPerson), CancellationToken.None);

        _auditLogs.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.UserLogout);
        written.UserId.Should().Be(userId, "the subject is the person whose sign-ins ended");
        written.PerformedBy.Should().Be(nextPerson, "the actor is the account that caused it");
        written.AdditionalData.Should().Contain("\"signedOutBy\":\"singleLogout\"")
            .And.Contain($"\"trigger\":\"{SingleLogoutTriggers.OtherUserSignedIn}\"")
            .And.Contain(idpSessionId.ToString())
            .And.Contain("\"endedSignInCount\":3");
    }

    [Fact]
    public async Task Handle_WhenTheWriteFails_LogsAndDoesNotThrow()
    {
        _auditLogs
            .Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("the database did not answer"));

        var act = () => Handler().Handle(Event(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        await act.Should().NotThrowAsync("the sign-ins have already ended; the sign-out must not fail on its record");
    }
}
