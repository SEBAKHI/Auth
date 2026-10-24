using Auth.Application.Common;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// OI-224: the three account rules, decided by <see cref="AccountActionPolicy"/> from
/// what was read. The handler suites (<c>AccountActionMatrixTests</c>) prove each
/// handler asks; these prove what the answer is.
/// </summary>
public class AccountActionPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILogger<AccountActionPolicy>> _logger = new();
    private readonly AccountActionPolicy _policy;

    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _superAdminRole = Guid.NewGuid();

    public AccountActionPolicyTests()
    {
        _policy = AccountRules.Policy(_permissions, _users, new FixedTimeProvider(Now), _logger.Object);
    }

    private void Holds(Guid userId, params string[] codes) =>
        _permissions
            .Setup(p => p.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);

    private void FullAdministrators(params FullAdministratorGrant[] grants) =>
        _permissions
            .Setup(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants);

    private Task<ErrorOr<Success>> Decide(AccountAction action) =>
        _policy.EnsureAllowedAsync(new AccountActionRequest(_actor, _account, action), CancellationToken.None);

    // --- Rule 1 -----------------------------------------------------------

    [Theory]
    [InlineData(AccountAction.Update, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.ProfileImage, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.Lock, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.Unlock, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.Activate, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.Deactivate, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.RemoveRole, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.RevokePermission, "User.CannotModifySystemUser")]
    [InlineData(AccountAction.Delete, "User.CannotDeleteSystemUser")]
    [InlineData(AccountAction.PermanentDelete, "User.CannotDeleteSystemUser")]
    [InlineData(AccountAction.RequestOwnDeletion, "User.CannotDeleteSystemUser")]
    public async Task EnsureAllowed_SystemAccount_IsRefusedWithItsCode_WhateverTheActorHolds(AccountAction action, string code)
    {
        Holds(_actor, "*");

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, WellKnownUserIds.System, action), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
    }

    // --- Rule 2 -----------------------------------------------------------

    [Theory]
    [InlineData(AccountAction.Update)]
    [InlineData(AccountAction.Unlock)]
    [InlineData(AccountAction.Activate)]
    [InlineData(AccountAction.Delete)]
    public async Task EnsureAllowed_AccountHoldingWhatTheActorDoesNot_IsNotCovered(AccountAction action)
    {
        Holds(_actor, "users:*");
        Holds(_account, "*");
        FullAdministrators(AccountRules.Wildcard(_account, _superAdminRole), AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole));

        var result = await Decide(action);

        result.FirstError.Should().Be(UserErrors.AuthorityNotCovered);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
    }

    /// <remarks>D1 (a): an account holding no platform permission is covered by any user administrator.</remarks>
    [Fact]
    public async Task EnsureAllowed_AccountHoldingNoPlatformPermission_IsCoveredByAUserAdministrator()
    {
        Holds(_actor, "users:*");
        Holds(_account);

        var result = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RemoveRole(_actor, _account, Guid.NewGuid(), applicationId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureAllowed_OnesOwnAccount_PassesTheCoverageRule()
    {
        Holds(_actor, "users:read");

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, _actor, AccountAction.Update), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _permissions.Verify(
            p => p.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never(),
            "one's own account needs nobody's grants read");
    }

    // --- Rule 3 -----------------------------------------------------------

    [Theory]
    [InlineData(AccountAction.Lock)]
    [InlineData(AccountAction.Deactivate)]
    [InlineData(AccountAction.Delete)]
    public async Task EnsureAllowed_TakingTheOnlyFullAdministratorAway_IsRefused(AccountAction action)
    {
        Holds(_actor, "*");
        Holds(_account, "*");
        // The actor holds * but does not count: an administrator's lock, with its
        // session not yet ended.
        FullAdministrators(
            AccountRules.Wildcard(_account, _superAdminRole),
            AccountRules.Wildcard(_actor, _superAdminRole, UserStatus.Locked));

        var result = await Decide(action);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
    }

    [Theory]
    [InlineData(AccountAction.Lock)]
    [InlineData(AccountAction.Deactivate)]
    [InlineData(AccountAction.Delete)]
    [InlineData(AccountAction.RequestOwnDeletion)]
    public async Task EnsureAllowed_TheOnlyFullAdministratorOnItself_IsRefused(AccountAction action)
    {
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole));

        var result = await _policy.EnsureAllowedAsync(new AccountActionRequest(_actor, _actor, action), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
    }

    [Fact]
    public async Task EnsureAllowed_RemovingTheOnlyPlatformWildcardRole_IsRefusedWithTheRoleCode()
    {
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole));

        var result = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RemoveRole(_actor, _actor, _superAdminRole, applicationId: null), CancellationToken.None);

        result.FirstError.Should().Be(RoleErrors.CannotRemoveLastAdminRole);
    }

    [Fact]
    public async Task EnsureAllowed_RevokingTheOnlyDirectPlatformWildcard_IsRefused()
    {
        FullAdministrators(AccountRules.Wildcard(_actor));

        var result = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RevokePermission(_actor, _actor, "*", applicationId: null), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
    }

    /// <remarks>The count is the one after the action: the account keeps * through its other way.</remarks>
    [Fact]
    public async Task EnsureAllowed_RemovingOneOfTwoWaysOfHoldingTheWildcard_IsAllowed()
    {
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), AccountRules.Wildcard(_actor));

        var role = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RemoveRole(_actor, _actor, _superAdminRole, applicationId: null), CancellationToken.None);
        var direct = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RevokePermission(_actor, _actor, "*", applicationId: null), CancellationToken.None);

        role.IsError.Should().BeFalse();
        direct.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureAllowed_RemovalsThatTakeNoPlatformWildcard_AreNotSubject()
    {
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole));

        // The same role at an application's scope, another permission, and * scoped
        // to an application: none is a way of holding platform *.
        (await _policy.EnsureAllowedAsync(
            AccountActionRequest.RemoveRole(_actor, _actor, _superAdminRole, Guid.NewGuid()), CancellationToken.None)).IsError.Should().BeFalse();
        (await _policy.EnsureAllowedAsync(
            AccountActionRequest.RevokePermission(_actor, _actor, "users:read", null), CancellationToken.None)).IsError.Should().BeFalse();
        (await _policy.EnsureAllowedAsync(
            AccountActionRequest.RevokePermission(_actor, _actor, "*", Guid.NewGuid()), CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Theory]
    [InlineData(AccountAction.Lock)]
    [InlineData(AccountAction.Deactivate)]
    [InlineData(AccountAction.Delete)]
    [InlineData(AccountAction.RequestOwnDeletion)]
    public async Task EnsureAllowed_WhileASecondFullAdministratorCanSignIn_IsAllowed(AccountAction action)
    {
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole));

        var result = await _policy.EnsureAllowedAsync(new AccountActionRequest(_actor, _actor, action), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    /// <summary>
    /// Whether the other holder of * counts: the sign-in path's own checks. An
    /// automatic lock (timed or hard) still counts — the owner signs in from a familiar
    /// source, renews, or resets the password — and only an administrator's lock does
    /// not (owner decision D-101-1 (a)). Deliberate break: count out every live lock
    /// (`!LockState.IsLive(now)`) → the timed and hard rows go red.
    /// </summary>
    [Theory]
    [InlineData(UserStatus.Active, 0, null, true, false, true)]
    [InlineData(UserStatus.Active, 0, null, true, true, false)]        // deleted, or waiting for deletion
    [InlineData(UserStatus.Inactive, 0, null, true, false, false)]
    [InlineData(UserStatus.Pending, 0, null, true, false, false)]
    [InlineData(UserStatus.Active, 0, null, false, false, false)]      // address not confirmed
    [InlineData(UserStatus.Locked, 0, null, true, false, false)]       // administrator's lock
    [InlineData(UserStatus.Locked, 3, 10, true, false, true)]          // timed lock, still ahead
    [InlineData(UserStatus.Locked, 20, null, true, false, true)]       // hard lock
    [InlineData(UserStatus.Locked, 3, -10, true, false, true)]         // lapsed: the next sign-in repairs it
    public async Task EnsureAllowed_TheOtherHolderCounts_OnlyIfItCouldSignInNow(
        UserStatus status, int failedAttempts, int? lockMinutesFromNow, bool emailConfirmed, bool isDeleted, bool counts)
    {
        var other = AccountRules.Wildcard(
            Guid.NewGuid(), _superAdminRole, status, failedAttempts,
            lockMinutesFromNow is { } minutes ? Now.AddMinutes(minutes) : null,
            emailConfirmed, isDeleted);
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), other);

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, _actor, AccountAction.Deactivate), CancellationToken.None);

        result.IsError.Should().Be(!counts);
    }

    /// <summary>
    /// OI-226 T5 and T6: the other holder of * counts only with a way to authenticate
    /// (a password, or a linked external sign-in) and a grant without an end date,
    /// even one still ahead. Deliberate break: count by the sign-in checks alone, as
    /// #101 did → the refused rows go red.
    /// </summary>
    [Theory]
    [InlineData(true, false, null, true)]       // a password, and * without an end date
    [InlineData(false, true, null, true)]       // a linked external sign-in instead
    [InlineData(true, true, null, true)]
    [InlineData(false, false, null, false)]     // no way to authenticate
    [InlineData(true, false, 60, false)]        // * ends in an hour
    [InlineData(true, false, 525600, false)]    // * ends in a year
    public async Task EnsureAllowed_TheOtherHolderCounts_OnlyWithACredentialAndWithoutAnEndDate(
        bool hasPassword, bool hasExternalLogin, int? expiresInMinutes, bool counts)
    {
        var other = AccountRules.Wildcard(
            Guid.NewGuid(), null,
            hasPassword: hasPassword,
            hasExternalLogin: hasExternalLogin,
            expiresAt: expiresInMinutes is { } minutes ? Now.AddMinutes(minutes) : null);
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), other);

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, _actor, AccountAction.Deactivate), CancellationToken.None);

        result.IsError.Should().Be(!counts);
        if (!counts)
        {
            result.FirstError.Code.Should().Be(UserErrors.LastFullAdministrator.Code);
        }
    }

    /// <summary>
    /// OI-226 T6: an account holding * both ways counts by its permanent grant.
    /// Removing that one while only an expiring grant remains takes the last full
    /// administrator away; revoking the expiring one takes nothing counted.
    /// </summary>
    [Fact]
    public async Task EnsureAllowed_AnAccountWithAPermanentAndAnExpiringWildcard_CountsByThePermanentOne()
    {
        FullAdministrators(
            AccountRules.Wildcard(_actor, _superAdminRole),
            AccountRules.Wildcard(_actor, null, expiresAt: Now.AddDays(1)));

        var removingThePermanent = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RemoveRole(_actor, _actor, _superAdminRole, null), CancellationToken.None);
        var revokingTheExpiring = await _policy.EnsureAllowedAsync(
            AccountActionRequest.RevokePermission(_actor, _actor, "*", null), CancellationToken.None);

        removingThePermanent.IsError.Should().BeTrue();
        removingThePermanent.FirstError.Code.Should().Be(RoleErrors.CannotRemoveLastAdminRole.Code);
        revokingTheExpiring.IsError.Should().BeFalse();
    }

    /// <summary>
    /// REVIEW 101 F2: an account that does not count cannot be the last. Acting on a
    /// holder of * that is deleted, inactive, administrator-locked or unconfirmed takes
    /// no full administrator away, even when nobody else counts either. Deliberate
    /// break: let the account's own grants count whatever its state → red.
    /// </summary>
    [Theory]
    [InlineData(AccountAction.Deactivate, UserStatus.Locked, true, false)]   // administrator's lock
    [InlineData(AccountAction.Lock, UserStatus.Inactive, true, false)]
    [InlineData(AccountAction.Delete, UserStatus.Active, false, false)]      // unconfirmed address
    [InlineData(AccountAction.PermanentDelete, UserStatus.Active, true, true)] // soft-deleted
    public async Task EnsureAllowed_AnAccountThatDoesNotCount_CannotBeTheLast(
        AccountAction action, UserStatus status, bool emailConfirmed, bool isDeleted)
    {
        Holds(_actor, "*");
        Holds(_account, "*");
        FullAdministrators(
            AccountRules.Wildcard(_account, _superAdminRole, status, emailConfirmed: emailConfirmed, isDeleted: isDeleted),
            AccountRules.Wildcard(_actor, _superAdminRole, emailConfirmed: false));

        (await Decide(action)).IsError.Should().BeFalse($"{action} on an account that does not count takes no full administrator away");
    }

    /// <remarks>An account that does not hold * takes nobody's away, whatever the count.</remarks>
    [Fact]
    public async Task EnsureAllowed_AccountWithoutTheWildcard_IsNotSubject_EvenWithNoFullAdministrator()
    {
        Holds(_actor, "*");
        Holds(_account, "users:read");
        FullAdministrators();

        (await Decide(AccountAction.Delete)).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureAllowed_ActionsThatAddNoneAndTakeNoneAway_AreNotSubject()
    {
        Holds(_actor, "*");
        Holds(_account, "*");
        FullAdministrators(AccountRules.Wildcard(_account, _superAdminRole, UserStatus.Inactive));

        foreach (var action in new[] { AccountAction.Update, AccountAction.ProfileImage, AccountAction.Unlock, AccountAction.Activate })
        {
            (await Decide(action)).IsError.Should().BeFalse($"{action} cannot take a full administrator away");
        }

        _permissions.Verify(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never(),
            "the full administrators are read only for the actions that can take one away");
    }

    // --- The locked overload, the log, the batch ------------------------------

    [Fact]
    public async Task Begin_DecidesOnTheGrantsReadUnderItsLock_NotOnAFreshRead_AndLeavesNothingOpen()
    {
        // A fresh read would say a second administrator exists; the locked read is
        // what the write will run under, and it says there is none.
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole));
        var change = _users.SetupAuthorityChange(AccountRules.Wildcard(_actor, _superAdminRole));

        var result = await _policy.BeginAsync(
            new AccountActionRequest(_actor, _actor, AccountAction.Deactivate), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
        change.Verify(c => c.DisposeAsync(), Times.Once(), "a refused change releases its lock at once");
        _permissions.Verify(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// OI-226 T3: removing * from a platform role is decided on the grants read under
    /// the lock its DELETE runs under, not on a fresh read, and a refusal releases it.
    /// </summary>
    [Fact]
    public async Task BeginRolePermissionRemoval_DecidesOnTheGrantsReadUnderItsLock_AndLeavesNothingOpen()
    {
        Holds(_actor, "*");
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), AccountRules.Wildcard(Guid.NewGuid(), null));
        var change = _users.SetupAuthorityChange(AccountRules.Wildcard(_actor, _superAdminRole));

        var result = await _policy.BeginRolePermissionRemovalAsync(
            new RolePermissionRemovalRequest(_actor, _superAdminRole, null, "*"), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
        change.Verify(c => c.DisposeAsync(), Times.Once(), "a refused change releases its lock at once");
        _permissions.Verify(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// OI-226 T1 (D-226-1 (a)): a remover who does not hold the code is refused before
    /// any lock, with the removal's own code, on any role — an application role
    /// included — and logged once.
    /// </summary>
    [Theory]
    [InlineData(null, "*")]
    [InlineData(null, "users:*")]
    [InlineData("app", "crm:orders.read")]
    public async Task BeginRolePermissionRemoval_ACodeTheActorDoesNotHold_IsRefusedBeforeTheLock(
        string? application, string code)
    {
        Holds(_actor, "roles:*", "permissions:*");
        _users.SetupAuthorityChange();

        var result = await _policy.BeginRolePermissionRemovalAsync(
            new RolePermissionRemovalRequest(_actor, Guid.NewGuid(), application is null ? null : Guid.NewGuid(), code),
            CancellationToken.None);

        result.FirstError.Should().Be(PermissionErrors.CannotRemoveHigherPermission);
        _users.Verify(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()), Times.Never(),
            "a refusal by the coverage rule takes no lock");
        _logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    /// <remarks>
    /// Review of PR 101 (M2): the reads rule 2 needs happen before the lock, so a
    /// refusal by rule 1 or 2 takes no lock at all, and an allowed change holds it
    /// only from the count to the commit.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Begin_RefusedByRuleOneOrTwo_TakesNoLock(int rule)
    {
        var account = rule == 1 ? WellKnownUserIds.System : _account;
        Holds(_actor, "users:*");
        Holds(account, "*");
        _users.SetupAuthorityChange();

        var result = await _policy.BeginAsync(new AccountActionRequest(_actor, account, AccountAction.Lock), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _users.Verify(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Begin_Allowed_ReturnsTheOpenChange_AfterReadingRuleTwoBeforeTheLock()
    {
        var order = new List<string>();
        Holds(_actor, "*");
        Holds(_account, "users:read");
        _permissions
            .Setup(p => p.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("permissions"))
            .ReturnsAsync(["*"]);
        var change = _users.SetupAuthorityChange();
        _users
            .Setup(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("lock"))
            .ReturnsAsync(change.Object);

        var result = await _policy.BeginAsync(new AccountActionRequest(_actor, _account, AccountAction.Lock), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeSameAs(change.Object);
        order.Should().Equal("permissions", "permissions", "lock");
        change.Verify(c => c.DisposeAsync(), Times.Never(), "the caller writes through it and commits");
    }

    [Theory]
    [InlineData(1, "User.CannotModifySystemUser")]
    [InlineData(2, "User.AuthorityNotCovered")]
    [InlineData(3, "User.LastFullAdministrator")]
    public async Task EnsureAllowed_ARefusal_LogsOneWarningNamingTheRuleAndTheAction(int rule, string code)
    {
        var account = rule == 1 ? WellKnownUserIds.System : _account;
        Holds(_actor, rule == 2 ? "users:*" : "*");
        Holds(account, "*");
        FullAdministrators(rule == 3
            ? [AccountRules.Wildcard(account, _superAdminRole)]
            : [AccountRules.Wildcard(account, _superAdminRole), AccountRules.Wildcard(_actor, _superAdminRole)]);

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, account, AccountAction.Deactivate), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        var warnings = _logger.Invocations.Where(i => i.Method.Name == "Log" && (LogLevel)i.Arguments[0] == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle();
        var message = warnings[0].Arguments[2]!.ToString();
        message.Should().Contain($"rule {rule}").And.Contain("Deactivate").And.Contain(account.ToString());
    }

    /// <summary>
    /// The batch the reads use answers exactly what the actions are answered — the
    /// same rules, in memory, from three reads whatever the number of accounts.
    /// </summary>
    [Fact]
    public async Task DecideFor_AnswersAsTheActionsAreAnswered_FromThreeReads()
    {
        var ordinary = Guid.NewGuid();
        var administrator = Guid.NewGuid();
        Holds(_actor, "users:*");
        _permissions
            .Setup(p => p.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>>
            {
                [ordinary] = [],
                [administrator] = ["*"],
                [WellKnownUserIds.System] = [],
            });
        FullAdministrators(AccountRules.Wildcard(administrator, _superAdminRole));

        var decisions = await _policy.DecideForAsync(_actor, [ordinary, administrator, WellKnownUserIds.System], CancellationToken.None);

        foreach (var action in Enum.GetValues<AccountAction>().Where(a => a != AccountAction.RequestOwnDeletion))
        {
            decisions.Allows(ordinary, action).Should().BeTrue($"{action} on an account holding nothing");
            decisions.Allows(administrator, action).Should().BeFalse($"{action} on an account holding *");
            decisions.Allows(WellKnownUserIds.System, action).Should().BeFalse($"{action} on the system account");
        }

        decisions.PermissionsOf(administrator).Should().Equal("*");
        _permissions.Verify(p => p.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once());
        _permissions.Verify(p => p.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once());
        _permissions.Verify(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Once());
    }
}
