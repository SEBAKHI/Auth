using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for staged account destruction: the claim race, the purge +
/// completion path, the idempotent already-purged path and the retry /
/// dead-letter accounting.
/// </summary>
public class ExecuteAccountDeletionCommandHandlerTests
{
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IImageStorageService> _imageStorageMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly ExecuteAccountDeletionCommandHandler _handler;

    public ExecuteAccountDeletionCommandHandlerTests()
    {
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _handler = new ExecuteAccountDeletionCommandHandler(
            _requestRepositoryMock.Object,
            _userRepositoryMock.Object,
            _imageStorageMock.Object,
            _publisherMock.Object,
            Options.Create(new AccountDeletionSettings()),
            new Mock<ILogger<ExecuteAccountDeletionCommandHandler>>().Object);
    }

    private AccountDeletionRequest SetupDueRequest(Guid? userId = null, int attemptCount = 0)
    {
        var request = new AccountDeletionRequest(
            id: Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            status: AccountDeletionStatus.PendingGrace,
            source: AccountDeletionSource.InApp,
            requestedAtUtc: DateTime.UtcNow.AddDays(-31),
            graceEndsAtUtc: DateTime.UtcNow.AddDays(-1),
            cancelledAtUtc: null,
            completedAtUtc: null,
            policyVersion: "2026.07",
            attemptCount: attemptCount,
            lastError: null,
            createdAt: DateTime.UtcNow.AddDays(-31),
            createdBy: userId ?? Guid.NewGuid());
        _requestRepositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        return request;
    }

    [Fact]
    public async Task Handle_DueRequest_PurgesCompletesAndPublishesSnapshot()
    {
        var request = SetupDueRequest();
        var user = TestHelpers.CreateUser(id: request.UserId, isDeleted: true, deletedAt: DateTime.UtcNow.AddDays(-31));
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Completed);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<AccountDeletionCompletedEvent>(e =>
                    e.UserId == request.UserId && e.PolicyVersion == "2026.07" && !e.ExternalRevocationFailed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RecoveryWonTheRace_SkipsWithoutPurging()
    {
        var request = SetupDueRequest();
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), AccountDeletionStatus.PendingGrace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.NotPendingGrace);
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<AccountDeletionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GraceNotElapsed_RefusesTheClaim()
    {
        var request = AccountDeletionRequest.Create(
            Guid.NewGuid(), AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", Guid.NewGuid());
        _requestRepositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.GraceNotElapsed);
    }

    [Fact]
    public async Task Handle_UserAlreadyPurged_CompletesIdempotentlyWithoutNotification()
    {
        var request = SetupDueRequest();
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Completed);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<AccountDeletionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PurgeFails_ReturnsToGraceQueueWithAttemptRecorded()
    {
        var request = SetupDueRequest();
        var user = TestHelpers.CreateUser(id: request.UserId, isDeleted: true, deletedAt: DateTime.UtcNow.AddDays(-31));
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.ExecutionFailed);
        request.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        request.AttemptCount.Should().Be(1);
        request.LastError.Should().Contain("db down");
    }

    [Fact]
    public async Task Handle_PurgeFailsAtAttemptCeiling_DeadLettersAsFailed()
    {
        var request = SetupDueRequest(attemptCount: 4);
        var user = TestHelpers.CreateUser(id: request.UserId, isDeleted: true, deletedAt: DateTime.UtcNow.AddDays(-31));
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(request.UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("persistent failure"));

        await _handler.Handle(new ExecuteAccountDeletionCommand(request.Id), CancellationToken.None);

        request.Status.Should().Be(AccountDeletionStatus.Failed);
        request.AttemptCount.Should().Be(5);
    }
}
