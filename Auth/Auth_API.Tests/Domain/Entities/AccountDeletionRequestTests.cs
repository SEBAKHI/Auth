using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// Unit tests for the <see cref="AccountDeletionRequest"/> entity.
/// Covers the factory, every legal and illegal lifecycle transition, and the
/// retry/dead-letter accounting.
/// </summary>
public class AccountDeletionRequestTests
{
    private const int MaxAttempts = 5;
    private readonly Guid _userId = Guid.NewGuid();

    #region Helper Methods

    /// <summary>
    /// Materializes a request in an arbitrary state. Grace defaults to already
    /// elapsed so claim-path tests exercise the status guard, not the clock.
    /// </summary>
    private AccountDeletionRequest CreateRequest(
        AccountDeletionStatus status = AccountDeletionStatus.PendingGrace,
        DateTime? graceEndsAtUtc = null,
        int attemptCount = 0)
    {
        var requestedAt = DateTime.UtcNow.AddDays(-30);
        return new AccountDeletionRequest(
            id: Guid.NewGuid(),
            userId: _userId,
            status: status,
            source: AccountDeletionSource.InApp,
            requestedAtUtc: requestedAt,
            graceEndsAtUtc: graceEndsAtUtc ?? DateTime.UtcNow.AddMinutes(-1),
            cancelledAtUtc: null,
            completedAtUtc: null,
            policyVersion: "2026.07",
            attemptCount: attemptCount,
            lastError: null,
            createdAt: requestedAt,
            createdBy: _userId);
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_ValidParameters_StartsPendingGraceWithComputedWindow()
    {
        var before = DateTime.UtcNow;
        var request = AccountDeletionRequest.Create(
            _userId, AccountDeletionSource.PublicWeb, TimeSpan.FromDays(30), "2026.07", _userId);
        var after = DateTime.UtcNow;

        request.UserId.Should().Be(_userId);
        request.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        request.Source.Should().Be(AccountDeletionSource.PublicWeb);
        request.RequestedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        request.GraceEndsAtUtc.Should().Be(request.RequestedAtUtc.AddDays(30));
        request.PolicyVersion.Should().Be("2026.07");
        request.AttemptCount.Should().Be(0);
        request.CancelledAtUtc.Should().BeNull();
        request.CompletedAtUtc.Should().BeNull();
        request.LastError.Should().BeNull();
        request.CreatedBy.Should().Be(_userId);
        request.IsActive.Should().BeTrue();
    }

    #endregion

    #region Claim Tests

    [Fact]
    public void Claim_PendingGraceAndGraceElapsed_TransitionsToProcessing()
    {
        var request = CreateRequest();

        var result = request.Claim();

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Processing);
        request.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Claim_GraceNotElapsed_ReturnsGraceNotElapsed()
    {
        var request = CreateRequest(graceEndsAtUtc: DateTime.UtcNow.AddDays(1));

        var result = request.Claim();

        result.FirstError.Should().Be(AccountDeletionErrors.GraceNotElapsed);
        request.Status.Should().Be(AccountDeletionStatus.PendingGrace);
    }

    [Theory]
    [InlineData(AccountDeletionStatus.Cancelled)]
    [InlineData(AccountDeletionStatus.Processing)]
    [InlineData(AccountDeletionStatus.Completed)]
    [InlineData(AccountDeletionStatus.Failed)]
    public void Claim_NotPendingGrace_ReturnsNotPendingGrace(AccountDeletionStatus status)
    {
        var request = CreateRequest(status);

        var result = request.Claim();

        result.FirstError.Should().Be(AccountDeletionErrors.NotPendingGrace);
        request.Status.Should().Be(status);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_PendingGrace_TransitionsToCancelledWithTimestamp()
    {
        var request = CreateRequest();

        var result = request.Cancel();

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Cancelled);
        request.CancelledAtUtc.Should().NotBeNull();
        request.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(AccountDeletionStatus.Processing)]
    [InlineData(AccountDeletionStatus.Completed)]
    [InlineData(AccountDeletionStatus.Failed)]
    public void Cancel_ClaimedOrTerminalExecution_ReturnsRecoveryWindowExpired(AccountDeletionStatus status)
    {
        var request = CreateRequest(status);

        var result = request.Cancel();

        result.FirstError.Should().Be(UserErrors.RecoveryWindowExpired);
        request.Status.Should().Be(status);
        request.CancelledAtUtc.Should().BeNull();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ReturnsNotPendingGrace()
    {
        var request = CreateRequest(AccountDeletionStatus.Cancelled);

        var result = request.Cancel();

        result.FirstError.Should().Be(AccountDeletionErrors.NotPendingGrace);
    }

    #endregion

    #region Complete Tests

    [Fact]
    public void Complete_Processing_TransitionsToCompletedWithTimestamp()
    {
        var request = CreateRequest(AccountDeletionStatus.Processing);

        var result = request.Complete();

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Completed);
        request.CompletedAtUtc.Should().NotBeNull();
        request.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(AccountDeletionStatus.PendingGrace)]
    [InlineData(AccountDeletionStatus.Cancelled)]
    [InlineData(AccountDeletionStatus.Completed)]
    [InlineData(AccountDeletionStatus.Failed)]
    public void Complete_NotProcessing_ReturnsNotProcessing(AccountDeletionStatus status)
    {
        var request = CreateRequest(status);

        var result = request.Complete();

        result.FirstError.Should().Be(AccountDeletionErrors.NotProcessing);
        request.Status.Should().Be(status);
    }

    #endregion

    #region Fail Tests

    [Fact]
    public void Fail_BelowAttemptCeiling_ReturnsToGraceQueueForRetry()
    {
        var request = CreateRequest(AccountDeletionStatus.Processing, attemptCount: 0);

        var result = request.Fail("apple revocation timed out", MaxAttempts);

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        request.AttemptCount.Should().Be(1);
        request.LastError.Should().Be("apple revocation timed out");
        request.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Fail_ReachingAttemptCeiling_DeadLettersAsFailed()
    {
        var request = CreateRequest(AccountDeletionStatus.Processing, attemptCount: MaxAttempts - 1);

        var result = request.Fail("persistent failure", MaxAttempts);

        result.IsError.Should().BeFalse();
        request.Status.Should().Be(AccountDeletionStatus.Failed);
        request.AttemptCount.Should().Be(MaxAttempts);
        request.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Fail_ErrorLongerThanColumn_TruncatesToMaxLength()
    {
        var request = CreateRequest(AccountDeletionStatus.Processing);
        var longError = new string('x', AccountDeletionRequest.MaxLastErrorLength + 500);

        request.Fail(longError, MaxAttempts);

        request.LastError.Should().HaveLength(AccountDeletionRequest.MaxLastErrorLength);
    }

    [Theory]
    [InlineData(AccountDeletionStatus.PendingGrace)]
    [InlineData(AccountDeletionStatus.Cancelled)]
    [InlineData(AccountDeletionStatus.Completed)]
    [InlineData(AccountDeletionStatus.Failed)]
    public void Fail_NotProcessing_ReturnsNotProcessing(AccountDeletionStatus status)
    {
        var request = CreateRequest(status);

        var result = request.Fail("error", MaxAttempts);

        result.FirstError.Should().Be(AccountDeletionErrors.NotProcessing);
        request.Status.Should().Be(status);
        request.AttemptCount.Should().Be(0);
    }

    #endregion

    #region IsActive Tests

    [Theory]
    [InlineData(AccountDeletionStatus.PendingGrace, true)]
    [InlineData(AccountDeletionStatus.Processing, true)]
    [InlineData(AccountDeletionStatus.Cancelled, false)]
    [InlineData(AccountDeletionStatus.Completed, false)]
    [InlineData(AccountDeletionStatus.Failed, false)]
    public void IsActive_ReflectsStatus(AccountDeletionStatus status, bool expected)
    {
        CreateRequest(status).IsActive.Should().Be(expected);
    }

    #endregion
}
