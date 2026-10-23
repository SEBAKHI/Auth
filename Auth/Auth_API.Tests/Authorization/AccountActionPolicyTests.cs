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
    private readonly Mock<ILogger<AccountActionPolicy>> _logger = new();
    private readonly AccountActionPolicy _policy;

    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _superAdminRole = Guid.NewGuid();

    public AccountActionPolicyTests()
    {
        _policy = AccountRules.Policy(_permissions, new FixedTimeProvider(Now), _logger.Object);
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
        // The actor holds * but cannot sign in from anywhere now: a stranger's wrong
        // passwords put a timed lock on it while its session lives on.
        FullAdministrators(
            AccountRules.Wildcard(_account, _superAdminRole),
            AccountRules.Wildcard(_actor, _superAdminRole, UserStatus.Locked, failedAttempts: 5, lockoutEndUtc: Now.AddMinutes(10)));

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
    /// Whether the other holder of * counts: the sign-in path's own checks, the
    /// stricter of the password sign-in and renewal (deviation S6).
    /// </summary>
    [Theory]
    [InlineData(UserStatus.Active, 0, null, true, false, true)]
    [InlineData(UserStatus.Active, 0, null, true, true, false)]        // deleted, or waiting for deletion
    [InlineData(UserStatus.Inactive, 0, null, true, false, false)]
    [InlineData(UserStatus.Pending, 0, null, true, false, false)]
    [InlineData(UserStatus.Active, 0, null, false, false, false)]      // address not confirmed
    [InlineData(UserStatus.Locked, 0, null, true, false, false)]       // administrator's lock
    [InlineData(UserStatus.Locked, 3, 10, true, false, false)]         // timed lock, still ahead
    [InlineData(UserStatus.Locked, 20, null, true, false, false)]      // hard lock
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
    public async Task EnsureAllowed_WithAChange_DecidesOnTheGrantsReadUnderItsLock_NotOnAFreshRead()
    {
        // A fresh read would say a second administrator exists; the locked read is
        // what the write will run under, and it says there is none.
        FullAdministrators(AccountRules.Wildcard(_actor, _superAdminRole), AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole));
        var change = new Mock<IUserRepository>().SetupAuthorityChange(AccountRules.Wildcard(_actor, _superAdminRole));

        var result = await _policy.EnsureAllowedAsync(
            new AccountActionRequest(_actor, _actor, AccountAction.Deactivate), change.Object, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
        _permissions.Verify(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never());
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
