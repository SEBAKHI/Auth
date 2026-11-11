using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.AccountDeletion;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for the deletion worker's due pass (execution + overdue alarm)
/// and the daily retention sweep (restore re-apply + retention enforcement,
/// once per UTC day, with an audit row only when the sweep changed something
/// or failed).
/// </summary>
public class AccountDeletionWorkerTests
{
    private const string SweepAction = "system.retention_sweep";

    // 10:30 UTC, so "now" and the start of the UTC day differ by hours and a
    // cutoff computed from "now" cannot pass for one computed from midnight.
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTime Midnight = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IAccountDeletionVerificationRepository> _verificationRepositoryMock = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock = new();
    private readonly Mock<INotificationOutboxRepository> _outboxRepositoryMock = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepositoryMock = new();
    private readonly Mock<IAccountDeletionTombstoneRepository> _tombstoneRepositoryMock = new();
    private readonly Mock<ISecretOperationChallengeRepository> _secretChallengeRepositoryMock = new();
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<ILogger<AccountDeletionWorker>> _loggerMock = new();
    private readonly AccountDeletionSettings _settings = new();
    private readonly AdjustableTimeProvider _time = new();
    private readonly RetentionSweepStatus _status;
    private readonly List<AuditLog> _auditRows = [];
    private readonly AccountDeletionWorker _worker;

    public AccountDeletionWorkerTests()
    {
        // Freeze first, then move: one read of the real clock, so "now" is exact.
        _time.Advance(TimeSpan.Zero);
        _time.Advance(Now - _time.GetUtcNow());
        _status = new RetentionSweepStatus(_time);

        _requestRepositoryMock
            .Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest>());
        _requestRepositoryMock
            .Setup(r => r.GetCompletedWithLiveUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest>());
        _senderMock
            .Setup(s => s.Send(It.IsAny<ExecuteAccountDeletionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);
        _auditLogRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => _auditRows.Add(log))
            .Returns(Task.CompletedTask);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IAccountDeletionRequestRepository))).Returns(_requestRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(IUserRepository))).Returns(_userRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ICredentialRevocationService))).Returns(_credentialRevocationMock.Object);
        provider.Setup(p => p.GetService(typeof(IAccountDeletionVerificationRepository))).Returns(_verificationRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ILoginAttemptRepository))).Returns(_loginAttemptRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(INotificationOutboxRepository))).Returns(_outboxRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(IAuditLogRepository))).Returns(_auditLogRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(IAccountDeletionTombstoneRepository))).Returns(_tombstoneRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ISecretOperationChallengeRepository))).Returns(_secretChallengeRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ISender))).Returns(_senderMock.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        _worker = new AccountDeletionWorker(
            scopeFactory.Object,
            TestHelpers.CreateOptions(_settings),
            _status,
            _time,
            _loggerMock.Object);
    }

    private static AccountDeletionRequest CreateDueRequest() => new(
        id: Guid.NewGuid(),
        userId: Guid.NewGuid(),
        status: AccountDeletionStatus.PendingGrace,
        source: AccountDeletionSource.InApp,
        requestedAtUtc: DateTime.UtcNow.AddDays(-31),
        graceEndsAtUtc: DateTime.UtcNow.AddDays(-1),
        cancelledAtUtc: null,
        completedAtUtc: null,
        policyVersion: "2026.07",
        attemptCount: 0,
        lastError: null,
        createdAt: DateTime.UtcNow.AddDays(-31),
        createdBy: Guid.NewGuid());

    private List<AuditLog> SweepRows() => _auditRows.Where(log => log.Action == SweepAction).ToList();

    private static JsonElement Data(AuditLog log) => JsonDocument.Parse(log.AdditionalData!).RootElement;

    [Fact]
    public async Task ExecuteDuePassAsync_SendsTheExecuteCommandForEveryDueRequest()
    {
        var first = CreateDueRequest();
        var second = CreateDueRequest();
        _requestRepositoryMock
            .Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), _settings.WorkerBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest> { first, second });

        await _worker.ExecuteDuePassAsync(CancellationToken.None);

        _senderMock.Verify(
            s => s.Send(It.Is<ExecuteAccountDeletionCommand>(c => c.RequestId == first.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _senderMock.Verify(
            s => s.Send(It.Is<ExecuteAccountDeletionCommand>(c => c.RequestId == second.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteDuePassAsync_RequestStillPendingPastTheAlarmThreshold_LogsError()
    {
        // The overdue probe uses batch size 1 with the shifted deadline.
        _requestRepositoryMock
            .Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest> { CreateDueRequest() });

        await _worker.ExecuteDuePassAsync(CancellationToken.None);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_ReappliesDestructionForRestoredAccounts()
    {
        var request = CreateDueRequest();
        _requestRepositoryMock
            .Setup(r => r.GetCompletedWithLiveUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest> { request });
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _worker.RunSweepAsync(CancellationToken.None);

        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(request.UserId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(r => r.DeleteAsync(request.UserId, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(request.UserId, It.IsAny<CancellationToken>()), Times.Once);
        _auditRows.Should().ContainSingle(log => log.Action == "user.deletion_reapplied");
        // The sweep's own row counts it too: a re-applied destruction is a change.
        Data(SweepRows().Single()).GetProperty("reappliedDeletions").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task RunSweepAsync_EnforcesEveryRetention_CountedFromTheStartOfTheUtcDay()
    {
        await _worker.RunSweepAsync(CancellationToken.None);

        _verificationRepositoryMock.Verify(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Secret step-up challenges are the same class of short-lived credential
        // row. The table definition claimed this sweep purged them while nothing
        // did, so they accumulated for the life of the deployment.
        _secretChallengeRepositoryMock.Verify(
            r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        _loginAttemptRepositoryMock.Verify(
            r => r.CleanupOldAttemptsAsync(Midnight.AddDays(-365), It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxRepositoryMock.Verify(
            r => r.DeleteSentOlderThanAsync(Midnight.AddDays(-180), It.IsAny<CancellationToken>()),
            Times.Once);
        // The audit history must have an end of life. CleanupOldLogsAsync existed
        // with no caller anywhere in the solution, so AuditLogs grew without any
        // ceiling — an undisclosed indefinite retention period, and the record
        // class that would outlive any finite identifier-reservation window.
        _auditLogRepositoryMock.Verify(
            r => r.CleanupOldLogsAsync(Midnight.AddDays(-1095), It.IsAny<CancellationToken>()),
            Times.Once);
        // Identifier reservations expire. The tombstone is a keyed digest of an
        // e-mail address under a key the system keeps, so deleting the row is the
        // only erasure route available — a permanent registry could not be
        // reconciled with the published policy.
        _tombstoneRepositoryMock.Verify(
            r => r.DeleteExpiredAsync(Midnight.AddDays(-1095), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_TwoRunsOnTheSameUtcDay_ShareOneCutoff()
    {
        // The API restarts several times a day and sweeps at every start. With
        // cutoffs counted from "now", each restart found the rows that had aged
        // out since the last run. From the start of the day, the later runs find
        // nothing the first one left, so they delete nothing and write nothing.
        await _worker.RunSweepAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(9));
        await _worker.RunSweepAsync(CancellationToken.None);

        _loginAttemptRepositoryMock.Verify(
            r => r.CleanupOldAttemptsAsync(Midnight.AddDays(-365), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _auditLogRepositoryMock.Verify(
            r => r.CleanupOldLogsAsync(Midnight.AddDays(-1095), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunSweepAsync_DeletedNothing_WritesNoAuditRow()
    {
        // The row used to be written on every run, empty or not: 236 of 1,312
        // rows in the development database over 49 days, one with a non-zero
        // count. A sweep with nothing to delete is a heartbeat, and a heartbeat
        // belongs to the readiness check, not to the audit log.
        await _worker.RunSweepAsync(CancellationToken.None);

        _auditRows.Should().BeEmpty();
        _status.LastSuccessUtc.Should().Be(Now.UtcDateTime,
            "the readiness check learns from the status that the sweep ran");
    }

    [Fact]
    public async Task RunSweepAsync_DeletedSomething_WritesOneRowWithEveryCount()
    {
        _verificationRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _secretChallengeRepositoryMock.Setup(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _loginAttemptRepositoryMock
            .Setup(r => r.CleanupOldAttemptsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(40);
        _outboxRepositoryMock
            .Setup(r => r.DeleteSentOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        _auditLogRepositoryMock
            .Setup(r => r.CleanupOldLogsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(70);
        _tombstoneRepositoryMock
            .Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _worker.RunSweepAsync(CancellationToken.None);

        var row = SweepRows().Should().ContainSingle().Subject;
        row.IsSuccess.Should().BeTrue();
        var data = Data(row);
        data.GetProperty("purgedDeletionCodes").GetInt32().Should().Be(2);
        data.GetProperty("purgedSecretChallenges").GetInt32().Should().Be(3);
        data.GetProperty("purgedLoginAttempts").GetInt32().Should().Be(40);
        data.GetProperty("purgedOutboxRows").GetInt32().Should().Be(5);
        // Deleting audit history is the change an auditor looks for first; the
        // row used to carry only the cutoff, never how many rows went.
        data.GetProperty("purgedAuditLogs").GetInt32().Should().Be(70);
        data.GetProperty("releasedReservations").GetInt32().Should().Be(1);
        data.GetProperty("reappliedDeletions").GetInt32().Should().Be(0);
        data.GetProperty("auditCutoffUtc").GetDateTime().Should().Be(Midnight.AddDays(-1095));
        data.TryGetProperty("failedStep", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RunSweepAsync_FailsAfterDeleting_WritesAFailureRowWithTheCountsSoFar()
    {
        // Login attempts were destroyed, then the audit purge failed. Without a
        // row here that destruction would leave no trace in the audit log.
        _loginAttemptRepositoryMock
            .Setup(r => r.CleanupOldAttemptsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(40);
        _auditLogRepositoryMock
            .Setup(r => r.CleanupOldLogsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated"));

        var act = () => _worker.RunSweepAsync(CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        var row = SweepRows().Should().ContainSingle().Subject;
        row.IsSuccess.Should().BeFalse();
        row.ErrorMessage.Should().Be("Retention sweep failed during audit-logs.");
        var data = Data(row);
        data.GetProperty("purgedLoginAttempts").GetInt32().Should().Be(40);
        data.GetProperty("failedStep").GetString().Should().Be("audit-logs");
        _status.LastSuccessUtc.Should().BeNull();
    }

    [Fact]
    public async Task RunSweepAsync_FailingOnEveryRetry_WritesOneFailureRowPerRunOfFailures()
    {
        // A database outage fails the sweep on every 15-minute retry. One row per
        // retry would be close to a hundred a day: the noise this change removes.
        _verificationRepositoryMock
            .SetupSequence(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("first"))
            .ThrowsAsync(new TimeoutException("second"))
            .ReturnsAsync(0)
            .ThrowsAsync(new TimeoutException("after a success"));

        for (var run = 0; run < 4; run++)
        {
            try
            {
                await _worker.RunSweepAsync(CancellationToken.None);
            }
            catch (TimeoutException)
            {
            }
        }

        // Failure, failure (no row), success (no row: nothing deleted), failure.
        SweepRows().Should().HaveCount(2).And.OnlyContain(log => log.IsSuccess == false);
    }

    [Fact]
    public async Task RunSweepAsync_StoppedByShutdownAfterDeleting_RecordsTheInterruption()
    {
        _outboxRepositoryMock
            .Setup(r => r.DeleteSentOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        _auditLogRepositoryMock
            .Setup(r => r.CleanupOldLogsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _worker.RunSweepAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var row = SweepRows().Should().ContainSingle().Subject;
        row.ErrorMessage.Should().Be("Retention sweep interrupted during audit-logs.");
        Data(row).GetProperty("purgedOutboxRows").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task RunSweepAsync_StoppedByShutdownBeforeDeletingAnything_WritesNothing()
    {
        _verificationRepositoryMock
            .Setup(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _worker.RunSweepAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _auditRows.Should().BeEmpty("an ordinary shutdown that destroyed nothing is not an event");
    }

    [Fact]
    public async Task RunSweepAsync_RetentionBelowTheFloorInAFile_IsRaisedToTheFloor()
    {
        // The console refuses these values, but a settings file or an environment
        // variable never passes through the console. 0 used to put the audit
        // cutoff at "now" and delete the whole audit history on the next start.
        _settings.AuditLogRetentionDays = 0;
        _settings.LoginAttemptRetentionDays = 0;
        _settings.OutboxRetentionDays = -5;
        _settings.IdentifierReservationDays = 0;

        await _worker.RunSweepAsync(CancellationToken.None);

        _auditLogRepositoryMock.Verify(
            r => r.CleanupOldLogsAsync(Midnight.AddDays(-1095), It.IsAny<CancellationToken>()), Times.Once);
        _loginAttemptRepositoryMock.Verify(
            r => r.CleanupOldAttemptsAsync(Midnight.AddDays(-30), It.IsAny<CancellationToken>()), Times.Once);
        _outboxRepositoryMock.Verify(
            r => r.DeleteSentOlderThanAsync(Midnight.AddDays(-30), It.IsAny<CancellationToken>()), Times.Once);
        _tombstoneRepositoryMock.Verify(
            r => r.DeleteExpiredAsync(Midnight.AddDays(-1095), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_RetentionAboveTheCeilingInAFile_IsHeldAtTheCeiling()
    {
        // A value past what DateTime.AddDays accepts used to throw on every run,
        // stopping the whole sweep for good: no re-applied deletions after a
        // restore, and delivered mail never deleted.
        _settings.AuditLogRetentionDays = int.MaxValue;
        _settings.LoginAttemptRetentionDays = int.MaxValue;
        _settings.OutboxRetentionDays = int.MaxValue;
        _settings.IdentifierReservationDays = int.MaxValue;

        await _worker.RunSweepAsync(CancellationToken.None);

        _auditLogRepositoryMock.Verify(
            r => r.CleanupOldLogsAsync(Midnight.AddDays(-3650), It.IsAny<CancellationToken>()), Times.Once);
        _loginAttemptRepositoryMock.Verify(
            r => r.CleanupOldAttemptsAsync(Midnight.AddDays(-3650), It.IsAny<CancellationToken>()), Times.Once);
        _tombstoneRepositoryMock.Verify(
            r => r.DeleteExpiredAsync(Midnight.AddDays(-3650), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_NeverReleasesAnIdentifierWhileRecordsKeyedToItSurvive()
    {
        // The window is derived, not chosen: an address may only be released
        // once every record still keyed to it has expired. An operator who
        // shortens the reservation below the audit-log retention must not be
        // able to release addresses early — the sweep raises the floor itself
        // rather than trusting the saved value.
        _settings.IdentifierReservationDays = 1095;
        _settings.AuditLogRetentionDays = 3650;

        await _worker.RunSweepAsync(CancellationToken.None);

        _tombstoneRepositoryMock.Verify(
            r => r.DeleteExpiredAsync(Midnight.AddDays(-3650), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDailySweepIfDueAsync_RunsAtMostOncePerUtcDay()
    {
        await _worker.RunDailySweepIfDueAsync(CancellationToken.None);
        await _worker.RunDailySweepIfDueAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(14));
        await _worker.RunDailySweepIfDueAsync(CancellationToken.None);

        _requestRepositoryMock.Verify(
            r => r.GetCompletedWithLiveUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
