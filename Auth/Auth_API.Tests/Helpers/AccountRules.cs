using Auth.Application.Common;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Test doubles for the account rules (<see cref="AccountActionPolicy"/>) and the
/// locked change the last of them is judged under.
/// </summary>
internal static class AccountRules
{
    /// <summary>
    /// A policy over <paramref name="permissions"/> whose reads find nothing unless a
    /// test says otherwise: no account holds a platform permission and nobody holds
    /// <c>*</c>, so every action on an account other than the system account passes.
    /// Set up specific answers on <paramref name="permissions"/> after calling this:
    /// Moq answers with the last matching setup. The page read answers, for each id,
    /// what the one-account read answers, so a test sets up one and both agree. A
    /// handler that writes under the lock gets its change from the policy, so pass the
    /// handler's own <paramref name="users"/> double.
    /// </summary>
    public static AccountActionPolicy Policy(
        Mock<IPermissionRepository>? permissions = null,
        Mock<IUserRepository>? users = null,
        TimeProvider? timeProvider = null,
        ILogger<AccountActionPolicy>? logger = null)
    {
        permissions ??= new Mock<IPermissionRepository>();
        permissions
            .Setup(p => p.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        permissions
            .Setup(p => p.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
                ids.Distinct().ToDictionary(
                    id => id,
                    id => permissions.Object.GetUserEffectivePermissionsAsync(id, cancellationToken).GetAwaiter().GetResult()));
        permissions
            .Setup(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new AccountActionPolicy(
            permissions.Object,
            (users ?? new Mock<IUserRepository>()).Object,
            timeProvider ?? TimeProvider.System,
            logger ?? new Mock<ILogger<AccountActionPolicy>>().Object);
    }

    /// <summary>
    /// Makes <paramref name="users"/> open a change over <paramref name="fullAdministrators"/>
    /// (none by default); its writes are set up and verified on the returned mock.
    /// </summary>
    public static Mock<IAccountAuthorityChange> SetupAuthorityChange(
        this Mock<IUserRepository> users,
        params FullAdministratorGrant[] fullAdministrators)
    {
        var change = new Mock<IAccountAuthorityChange>();
        change.SetupGet(c => c.FullAdministratorGrants).Returns(fullAdministrators);
        change.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        users
            .Setup(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(change.Object);
        return change;
    }

    /// <summary>
    /// A platform grant of <c>*</c> to <paramref name="userId"/> through
    /// <paramref name="roleId"/> (null: the direct grant), the account active and
    /// confirmed unless a test says otherwise.
    /// </summary>
    public static FullAdministratorGrant Wildcard(
        Guid userId,
        Guid? roleId = null,
        UserStatus status = UserStatus.Active,
        int failedAttempts = 0,
        DateTime? lockoutEndUtc = null,
        bool emailConfirmed = true,
        bool isDeleted = false) =>
        new(userId, new AccountLockState(status, failedAttempts, lockoutEndUtc), emailConfirmed, isDeleted, roleId);
}
