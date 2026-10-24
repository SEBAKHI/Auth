using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// OI-224 T10 (the guard OI-118 proposed): every administrative action on another
/// account asks the account rules (<c>AccountActionPolicy</c>), and so does the
/// account's own deletion. The handlers that ask, the reads that tell the console
/// what to offer, and the writes that run under the last-administrator lock are each
/// listed here; a new endpoint that changes another account fails until it asks the
/// rules or is listed with the guard it has instead.
/// </summary>
/// <remarks>
/// Deliberate breaks: remove the policy call (and its constructor parameter) from
/// <c>UnlockAccountCommandHandler</c>; add an <c>[HttpPost("{id:guid}/x")]</c>
/// action sending a new command; write a lock through <c>_userRepository</c> again.
/// </remarks>
public class AccountActionPolicyGuardTests
{
    private static readonly List<(string Name, string Source)> Sources = ApiSourceScan.ProductionSources()
        // Line endings normalized: a Windows checkout has CRLF, the repository LF.
        .Select(file => (Path.GetFileName(file.File), file.Source.Replace("\r\n", "\n")))
        .ToList();

    private static string Source(string name) => Sources.Single(s => s.Name == name).Source;

    /// <summary>Every caller of the policy, with what it asks.</summary>
    private static readonly Dictionary<string, string> PolicyCallers = new()
    {
        // The actions of OI-118, each by its own handler.
        ["UpdateUserCommandHandler.cs"] = "AccountAction.Update)",
        ["SetProfileImageCommandHandler.cs"] = "AccountAction.ProfileImage)",
        ["RemoveProfileImageCommandHandler.cs"] = "AccountAction.ProfileImage)",
        ["DeleteUserCommandHandler.cs"] = "AccountAction.Delete)",
        ["HardDeleteUserCommandHandler.cs"] = "AccountAction.PermanentDelete)",
        ["RemoveUserRoleCommandHandler.cs"] = "AccountActionRequest.RemoveRole(",
        ["RevokeUserPermissionCommandHandler.cs"] = "AccountActionRequest.RevokePermission(",
        ["LockAccountCommandHandler.cs"] = "AccountAction.Lock)",
        ["UnlockAccountCommandHandler.cs"] = "AccountAction.Unlock)",
        ["ActivateAccountCommandHandler.cs"] = "AccountAction.Activate)",
        ["DeactivateAccountCommandHandler.cs"] = "AccountAction.Deactivate)",

        // The account's own deletion: the one pipeline behind the in-app request and
        // the public page (RequestAccountDeletion, ConfirmPublicDeletion).
        ["AccountDeletionRequestor.cs"] = "AccountAction.RequestOwnDeletion)",

        // The reads that tell the console which controls to show (T5).
        ["GetUserByIdQueryHandler.cs"] = "_accountActionPolicy.DecideForAsync(",
        ["GetUsersQueryHandler.cs"] = "_accountActionPolicy.DecideForAsync(",
        ["GetUserRolesQueryHandler.cs"] = "_accountActionPolicy.DecideForAsync(",
        ["GetUserPermissionsQueryHandler.cs"] = "_accountActionPolicy.DecideForAsync(",

        // OI-226: removing a permission from a role, and what the role read offers to
        // remove (T4).
        ["RevokeRolePermissionCommandHandler.cs"] = "new RolePermissionRemovalRequest(",
        ["GetRoleByIdQueryHandler.cs"] = "_accountActionPolicy.RemovableRolePermissionsAsync(",
    };

    /// <summary>
    /// The endpoints that change another account without the policy, each with the
    /// guard it has instead. They hand authority over rather than take it away.
    /// </summary>
    private static readonly Dictionary<string, string> OtherAccountWritesWithTheirOwnGuard = new()
    {
        ["AssignRoleCommand"] = "hands a role over: PermissionGrantGuard (no amplification) and PlatformGrantFactorGuard",
        ["GrantUserPermissionCommand"] = "hands a permission over: PermissionGrantGuard (no amplification)",
        ["ResetUserTwoFactorCommand"] = "its own rules 1 and 2 (EnsureTwoFactorResettableBy, PermissionGrantGuard); a reset takes no administration away",
    };

    /// <summary>
    /// The writes that can leave the platform without a full administrator, run
    /// through the locked change. The permanent delete is not among them: it only
    /// reaches a soft-deleted account, which cannot sign in and was never counted.
    /// </summary>
    private static readonly string[] LockedWriters =
    [
        "DeleteUserCommandHandler.cs", "RemoveUserRoleCommandHandler.cs", "RevokeUserPermissionCommandHandler.cs",
        "LockAccountCommandHandler.cs", "DeactivateAccountCommandHandler.cs", "AccountDeletionRequestor.cs",
        // OI-226: every removal from a role, whatever the code, so the DELETE has one path.
        "RevokeRolePermissionCommandHandler.cs",
    ];

    /// <summary>
    /// Callers of the repository's unlocked writes of the same kinds, each with why
    /// it needs no lock. The status write of Unlock and Activate is pinned by
    /// <c>LockStatementCallersSourceGuardTests</c>: they add a full administrator,
    /// never take one away.
    /// </summary>
    private static readonly Dictionary<string, string> UnlockedWriters = new()
    {
        ["AccountDeletionWorker.cs"] = "re-applies a deletion a backup restore undid: the account's own completed request, no administrator's act",
    };

    [Fact]
    public void EveryCallerOfThePolicy_IsListed_AndAsksItsAction()
    {
        var injectors = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\bAccountActionPolicy\s+\w+\s*[,)]"))
            .Select(s => s.Name)
            .ToList();

        injectors.Should().NotBeEmpty("the scan must find the known callers, or it proves nothing");
        injectors.Should().BeEquivalentTo(PolicyCallers.Keys,
            "a handler that acts on an account asks the account rules, and is listed here with what it asks");

        foreach (var (file, asks) in PolicyCallers)
        {
            Source(file).Should().Contain(asks, $"{file} asks the policy about its own action");
        }
    }

    [Fact]
    public void EveryUsersEndpointThatChangesAnotherAccount_AsksThePolicy_OrIsListedWithItsOwnGuard()
    {
        // Every write route that names an identifier, whatever the parameter is called
        // and wherever the segment sits (REVIEW 101 F3).
        var controller = Source("UsersController.cs");
        var endpoints = Regex.Matches(controller, @"\[Http(?:Put|Post|Delete|Patch)\(""[^""]*\{\w+:guid\}[^""]*""\)\]")
            .Select(attribute =>
            {
                var action = controller.IndexOf("public async Task<IActionResult>", attribute.Index, StringComparison.Ordinal);
                var nextAction = controller.IndexOf("public async Task<IActionResult>", action + 1, StringComparison.Ordinal);
                var body = controller[action..(nextAction < 0 ? controller.Length : nextAction)];
                return Regex.Match(body, @"\bnew (?<command>\w+Command)\(").Groups["command"].Value;
            })
            .ToList();

        endpoints.Should().HaveCountGreaterThan(10, "the scan must find the account endpoints, or it proves nothing");
        endpoints.Should().NotContain(string.Empty, "every endpoint on another account sends a command this guard can place");

        foreach (var command in endpoints)
        {
            if (OtherAccountWritesWithTheirOwnGuard.ContainsKey(command))
            {
                continue;
            }

            // The handler itself asks: listed, injected, and asking about its own action.
            var handler = $"{command}Handler.cs";
            PolicyCallers.Should().ContainKey(handler,
                $"{command} changes another account: its handler asks AccountActionPolicy, or it is listed with the guard it has instead");
            Source(handler).Should().MatchRegex(@"\bAccountActionPolicy\s+\w+\s*[,)]", $"{handler} takes the policy");
            Source(handler).Should().Contain(PolicyCallers[handler], $"{handler} asks the policy about its own action");
        }

        OtherAccountWritesWithTheirOwnGuard.Keys.Should().BeSubsetOf(endpoints, "a listed exception that no endpoint sends pins nothing");
    }

    /// <summary>
    /// The account's own deletion acts on an account through routes that name no
    /// identifier — the in-app request and the public page — so the scan above cannot
    /// see it. Both reach the one pipeline, which asks the rules before the code is
    /// spent and again under the lock (REVIEW 101 F3).
    /// </summary>
    [Fact]
    public void TheAccountsOwnDeletion_ReachesThePolicy_FromBothEntryPoints()
    {
        Source("UsersController.cs").Should().Contain("new RequestAccountDeletionCommand(userId, request.OtpCode)");
        Source("RequestAccountDeletionCommandHandler.cs").Should()
            .Contain("_requestor.EnsureRequestableAsync(user, cancellationToken)")
            .And.Contain("_requestor.RequestAsync(user, AccountDeletionSource.InApp, cancellationToken)");
        Source("ConfirmPublicDeletionCommandHandler.cs").Should()
            .Contain("_requestor.RequestAsync(user, AccountDeletionSource.PublicWeb, cancellationToken)");

        var requestor = Source("AccountDeletionRequestor.cs");
        requestor.Should().MatchRegex(@"_accountActionPolicy\.EnsureAllowedAsync\(\s*new AccountActionRequest\(user\.Id, user\.Id, AccountAction\.RequestOwnDeletion\)",
            "the precheck asks the rules before the single-use code is spent");
        requestor.Should().MatchRegex(@"_accountActionPolicy\.BeginAsync\(\s*new AccountActionRequest\(user\.Id, user\.Id, AccountAction\.RequestOwnDeletion\)",
            "the deletion itself is decided and written under the lock");

        // Nothing else writes a deletion request: every receiver declared as the request
        // repository, and the TryCreateAsync called on it.
        Sources
            .Where(s => Regex.Matches(s.Source, @"\bIAccountDeletionRequestRepository\s+(\w+)\s*[,;)=]")
                .Any(declared => Regex.IsMatch(s.Source, $@"\b{Regex.Escape(declared.Groups[1].Value)}\s*\.\s*TryCreateAsync\s*\(")))
            .Select(s => s.Name)
            .Should().BeEquivalentTo(["AccountDeletionRequestor.cs"],
                "a deletion request is created only by the pipeline that asks the rules");
    }

    [Fact]
    public void TheWritesThatCanTakeTheLastFullAdministratorAway_RunUnderTheLock()
    {
        // The lock is opened in one place, the policy, which decides all three rules
        // before handing the change over; a writer gets the change only from it.
        Sources
            .Where(s => Regex.IsMatch(s.Source, @"\.\s*BeginAuthorityChangeAsync\s*\("))
            .Select(s => s.Name)
            .Should().BeEquivalentTo(["AccountActionPolicy.cs"]);
        Sources
            .Where(s => Regex.IsMatch(s.Source, @"\b_accountActionPolicy\s*\.\s*Begin\w*Async\s*\("))
            .Select(s => s.Name)
            .Should().BeEquivalentTo(LockedWriters);

        foreach (var file in LockedWriters)
        {
            Source(file).Should().MatchRegex(@"\b(change|authorityChange)\.Commit\(\);",
                $"{file} commits the locked change");
        }

        // Removing a role or revoking a permission has no unlocked path at all: the
        // repository no longer offers one (review of PR 101, L4). A soft delete still
        // does, for the deletion worker; find it by the receiver's declared type as
        // well as by name.
        typeof(Auth.Domain.Interfaces.Repositories.IUserRepository).GetMethods().Select(m => m.Name)
            .Should().NotContain(["RemoveRoleAsync", "RevokePermissionAsync"]);
        var unlocked = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\b_?userRepository\s*\.\s*DeleteAsync\s*\(")
                || Regex.Matches(s.Source, @"\bIUserRepository\s+(\w+)\s*[,;)=]")
                    .Any(declared => Regex.IsMatch(s.Source, $@"\b{Regex.Escape(declared.Groups[1].Value)}\s*\.\s*DeleteAsync\s*\(")))
            .Select(s => s.Name);
        unlocked.Should().BeEquivalentTo(UnlockedWriters.Keys,
            "soft-deleting an account goes through the locked change");

        // OI-226 T3: a permission leaves a role only through the change. The repository
        // offers no unlocked removal, the one DELETE lives in one statement, and only
        // the change calls it.
        typeof(Auth.Domain.Interfaces.Repositories.IPermissionRepository).GetMethods().Select(m => m.Name)
            .Should().NotContain("RevokeFromRoleAsync");
        // Counted across the whole tree, the defining file included (REVIEW 102 F4). A
        // DELETE on the table, with or without FROM, an alias, the dbo schema or
        // brackets: exactly one statement.
        Sources
            .SelectMany(s => Regex.Matches(
                    s.Source, @"DELETE\s+(\w+\s+)?(FROM\s+)?(\[?dbo\]?\.)?\[?RolePermissions\b", RegexOptions.IgnoreCase)
                .Select(_ => s.Name))
            .Should().Equal(["PermissionRepository.cs"], "a permission leaves a role through one statement");

        // That statement's method: its definition and the locked change's forwarding
        // line, nothing else — no wrapper that opens its own connection.
        Sources
            .SelectMany(s => Regex.Matches(s.Source, @"\bDeleteRolePermissionAsync\s*\(").Select(_ => s.Name))
            .Should().BeEquivalentTo(["PermissionRepository.cs", "UserRepository.cs"],
                "only the locked change calls the role-grant DELETE");

        // And the change's removal is asked for by the one handler that decides it: a
        // change opened for an account action must not remove * from a role.
        Sources
            .Where(s => Regex.IsMatch(s.Source, @"\.\s*RevokeFromRoleAsync\s*\("))
            .Select(s => s.Name)
            .Should().BeEquivalentTo(["RevokeRolePermissionCommandHandler.cs"],
                "only the role removal, decided by BeginRolePermissionRemovalAsync, removes from a role");
    }

    [Fact]
    public void TheRoleRead_ComputesWhatTheCallerMayRemove()
    {
        Source("RolesController.cs").Should().Contain("new GetRoleByIdQuery(id) { ActorId = GetCurrentUserId() };",
            "without the caller the role read computes nothing removable, and the console shows every remove control");
    }

    [Theory]
    [InlineData("new GetUserByIdQuery(id) { ActorId = GetCurrentUserId() };")]
    [InlineData("new GetUserRolesQuery(id, sortBy, sortDirection) { ActorId = GetCurrentUserId() };")]
    [InlineData("new GetUserPermissionsQuery(id, sortBy, sortDirection) { ActorId = GetCurrentUserId() };")]
    [InlineData("new GetUserByIdQuery(userId) { ActorId = userId };")]
    [InlineData("new GetUsersQuery(pageNumber, pageSize, searchTerm, sortBy, sortDirection, includeDeleted)\n        {\n            ActorId = GetCurrentUserId()\n        };")]
    public void TheUserReads_ComputeTheCallersAllowedActions(string query)
    {
        Source("UsersController.cs").Should().Contain(query,
            "without the caller the reads compute no allowed actions, and the console shows every control");
    }
}
