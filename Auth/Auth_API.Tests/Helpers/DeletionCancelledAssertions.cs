using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// REVIEW 151 F1, owner decision D-151-1 (b): what every recovery entry does for an account in its
/// own grace period whose state refuses a session (deactivated, not yet activated, locked by an
/// administrator). The proven recovery is accepted — the request is written cancelled, the
/// account restored and the cancellation published, once each — and nothing else happens: the
/// builder, which writes the session, the sign-in row and the lift of a lock, never runs, and the
/// account's status and lock are written by nothing.
/// </summary>
internal static class DeletionCancelledAssertions
{
    /// <summary>The answers, by the state that refuses the session.</summary>
    public static TheoryData<string, UserStatus, int, int?, string> StatesThatRefuseASession => new()
    {
        // Why, the status, the failure count, minutes until the lock ends (null: none), the answer.
        { "deactivated", UserStatus.Inactive, 0, null, "User.DeletionCancelledAccountInactive" },
        { "not yet activated", UserStatus.Pending, 0, null, "User.DeletionCancelledAccountPending" },
        { "locked by an administrator", UserStatus.Locked, 0, null, "User.DeletionCancelledAccountLocked" },
        { "locked by an administrator until a date", UserStatus.Locked, 0, 60, "User.DeletionCancelledAccountLocked" },
    };

    /// <summary>The lock's end for a row of <see cref="StatesThatRefuseASession"/>.</summary>
    public static DateTime? LockEnd(int? minutes) => minutes is { } m ? DateTime.UtcNow.AddMinutes(m) : null;

    public static void VerifyCancelledWithoutASession(
        Mock<IAccountDeletionRequestRepository> requests,
        Mock<IUserRepository> users,
        Mock<ILoginResponseBuilder> builder,
        Mock<IPublisher> publisher,
        Guid userId,
        string because)
    {
        requests.Verify(r => r.UpdateAsync(
                It.Is<AccountDeletionRequest>(request => request.Status == AccountDeletionStatus.Cancelled),
                AccountDeletionStatus.PendingGrace,
                It.IsAny<CancellationToken>()),
            Times.Once, $"{because}: the deletion is cancelled");
        users.Verify(u => u.RestoreAsync(userId, It.IsAny<CancellationToken>()), Times.Once, $"{because}: the account is restored");
        publisher.Verify(p => p.Publish(
                It.Is<AccountDeletionCancelledEvent>(cancelled => cancelled.UserId == userId), It.IsAny<CancellationToken>()),
            Times.Once, $"{because}: its owner hears of it, as for every recovery");
        builder.Invocations.Should().BeEmpty($"{because}: no session, no sign-in row, no lock lifted");
        users.Invocations
            .Select(invocation => invocation.Method.Name)
            .Where(name => !name.StartsWith("Get", StringComparison.Ordinal))
            .Should().Equal(["RestoreAsync"], $"{because}: the restore writes the deletion columns only, and nothing writes the status or the lock");
    }
}
