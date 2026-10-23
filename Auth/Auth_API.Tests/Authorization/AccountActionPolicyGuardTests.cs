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
        var controller = Source("UsersController.cs");
        var endpoints = Regex.Matches(controller, @"\[Http(?:Put|Post|Delete|Patch)\(""\{id:guid\}[^""]*""\)\]")
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
            var handler = $"{command}Handler.cs";
            (PolicyCallers.ContainsKey(handler) || OtherAccountWritesWithTheirOwnGuard.ContainsKey(command))
                .Should().BeTrue($"{command} changes another account: its handler asks AccountActionPolicy, or it is listed with the guard it has instead");
        }

        OtherAccountWritesWithTheirOwnGuard.Keys.Should().BeSubsetOf(endpoints, "a listed exception that no endpoint sends pins nothing");
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
            .Where(s => Regex.IsMatch(s.Source, @"\b_accountActionPolicy\s*\.\s*BeginAsync\s*\("))
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
