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

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for the deletion worker's due pass (execution + overdue alarm)
/// and the daily retention sweep (restore re-apply + retention enforcement,
/// once per UTC day).
/// </summary>
public class AccountDeletionWorkerTests
{
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IAccountDeletionVerificationRepository> _verificationRepositoryMock = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock = new();
    private readonly Mock<INotificationOutboxRepository> _outboxRepositoryMock = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepositoryMock = new();
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<ILogger<AccountDeletionWorker>> _loggerMock = new();
    private readonly AccountDeletionSettings _settings = new();
    private readonly AccountDeletionWorker _worker;

    public AccountDeletionWorkerTests()
    {
        _requestRepositoryMock
            .Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest>());
        _requestRepositoryMock
            .Setup(r => r.GetCompletedWithLiveUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDeletionRequest>());
        _senderMock
            .Setup(s => s.Send(It.IsAny<ExecuteAccountDeletionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IAccountDeletionRequestRepository))).Returns(_requestRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(IUserRepository))).Returns(_userRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ICredentialRevocationService))).Returns(_credentialRevocationMock.Object);
        provider.Setup(p => p.GetService(typeof(IAccountDeletionVerificationRepository))).Returns(_verificationRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ILoginAttemptRepository))).Returns(_loginAttemptRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(INotificationOutboxRepository))).Returns(_outboxRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(IAuditLogRepository))).Returns(_auditLogRepositoryMock.Object);
        provider.Setup(p => p.GetService(typeof(ISender))).Returns(_senderMock.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        _worker = new AccountDeletionWorker(
            scopeFactory.Object,
            Options.Create(_settings),
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
        _auditLogRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<AuditLog>(log => log.Action == "user.deletion_reapplied"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_EnforcesEveryRetention_AndWritesTheSweepAudit()
    {
        await _worker.RunSweepAsync(CancellationToken.None);

        _verificationRepositoryMock.Verify(r => r.DeleteExpiredAsync(It.IsAny<CancellationToken>()), Times.Once);
        _loginAttemptRepositoryMock.Verify(
            r => r.CleanupOldAttemptsAsync(
                It.Is<DateTime>(cutoff => Math.Abs((cutoff - DateTime.UtcNow.AddDays(-365)).TotalMinutes) < 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxRepositoryMock.Verify(
            r => r.DeleteSentOlderThanAsync(
                It.Is<DateTime>(cutoff => Math.Abs((cutoff - DateTime.UtcNow.AddDays(-180)).TotalMinutes) < 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditLogRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<AuditLog>(log => log.Action == "system.retention_sweep"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunDailySweepIfDueAsync_RunsAtMostOncePerUtcDay()
    {
        await _worker.RunDailySweepIfDueAsync(CancellationToken.None);
        await _worker.RunDailySweepIfDueAsync(CancellationToken.None);

        _requestRepositoryMock.Verify(
            r => r.GetCompletedWithLiveUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
