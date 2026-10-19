using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// S08 T11: withholding platform authority from a token is only as good as the
/// places that read platform authority somewhere else. Every live reader of a
/// user's platform permissions is listed here with why it cannot hand back what
/// the token was minted without; a new one fails this guard until it is.
/// </summary>
/// <remarks>
/// Two ways a live reader is safe: it sits behind a platform <c>[RequirePermission]</c>
/// code, which the withheld token cannot satisfy (the route-aware organization
/// fallback applies to <c>org:</c> codes only); or it takes the token's word as well
/// (<c>platformAuthorityInToken</c>, the credential-stats claims) — claim AND live grant.
/// </remarks>
public class LivePlatformAuthorityGuardTests
{
    private static readonly List<(string Name, string Source)> Sources = ApiSourceScan.ProductionSources()
        // Line endings normalized: a Windows checkout has CRLF, the repository LF.
        .Select(file => (Path.GetFileName(file.File), file.Source.Replace("\r\n", "\n")))
        .ToList();

    private static string Source(string name) => Sources.Single(s => s.Name == name).Source;

    [Fact]
    public void TheOnlyPermissionCheckerConsumer_IsCredentialStats_AndItAsksTheTokenFirst()
    {
        var consumers = Sources
            .Where(s => s.Name is not ("IPermissionChecker.cs" or "PermissionChecker.cs" or "Program.cs"))
            .Where(s => Regex.IsMatch(s.Source, @"\bIPermissionChecker\b"))
            .Select(s => s.Name);

        consumers.Should().BeEquivalentTo(["GetCredentialStatsQueryHandler.cs"],
            "a new live permission check must take the token's word too, or a withheld session reads around it");

        var handler = Source("GetCredentialStatsQueryHandler.cs");
        handler.Should().Contain("request.TokenGrantsApiKeysRead\n            && await _permissionChecker.HasPermissionAsync(")
            .And.Contain("request.TokenGrantsWebhookKeysRead\n            && await _permissionChecker.HasPermissionAsync(");

        var controller = Source("DashboardController.cs");
        controller.Should().Contain("TokenGrantsApiKeysRead = HasPermissionClaim(PermissionCodes.ApiKeys.Read)")
            .And.Contain("TokenGrantsWebhookKeysRead = HasPermissionClaim(PermissionCodes.WebhookKeys.Read)");
    }

    /// <summary>
    /// Callers of the platform form of <c>GetUserEffectivePermissionsAsync(userId, ct)</c> in
    /// Application, each with why it is safe.
    /// </summary>
    /// <summary>
    /// Every method that reads a user's roles or permissions live, by the interface
    /// that offers it. A call is found by its receiver's TYPE — an identifier
    /// declared as one of these interfaces, then any of these methods called on it —
    /// so neither a field's name nor an argument's name lets a new reader through.
    /// </summary>
    private static readonly Dictionary<Type, string[]> GrantReads = new()
    {
        [typeof(IPermissionRepository)] = ["GetUserEffectivePermissionsAsync", "UserHasPermissionAsync", "GetUsersHoldingPlatformPermissionsAsync"],
        [typeof(IPermissionChecker)] = ["HasPermissionAsync", "HasAnyPermissionAsync", "HasAllPermissionsAsync", "GetUserPermissionsAsync"],
        [typeof(IRoleRepository)] = ["GetUserRolesAsync", "GetUserRolesForApplicationAsync", "UserHasRoleAsync"],
        [typeof(IUserRepository)] = ["GetUserRolesAsync", "GetUserRoleAsync", "HasRoleAsync", "GetUserPermissionsAsync", "GetUserPermissionAsync", "HasDirectPermissionAsync"],
    };

    /// <summary>The interfaces and their implementations: where the reads are defined, not used.</summary>
    private static readonly string[] GrantReadDefinitions =
    [
        "IPermissionRepository.cs", "PermissionRepository.cs", "IPermissionChecker.cs", "PermissionChecker.cs",
        "IRoleRepository.cs", "RoleRepository.cs", "IUserRepository.cs", "UserRepository.cs",
    ];

    private static readonly Dictionary<string, string> PlatformReaders = new()
    {
        ["TokenClaimsResolver.cs"] = "the mint itself: what the policy then decides on",
        ["PermissionGrantGuard.cs"] = "every caller sits behind a platform [RequirePermission] code (pinned below)",
        ["OrganizationGrantGuard.cs"] = "reachable on org_perm alone, so it takes platformAuthorityInToken (pinned below)",
        ["GetCredentialStatsQueryHandler.cs"] = "asks the token first (pinned above)",
        ["ExternalLoginCommandHandler.cs"] = "the subject's own grants, for a warning on its own link event; authorizes nothing",
        ["CreateUserCommandHandler.cs"] = "the created user's grants, for the response; authorizes nothing",
        ["GetUserByIdQueryHandler.cs"] = "the target user's grants, for display; authorizes nothing",
        ["GetUsersQueryHandler.cs"] = "the listed users' grants, for display; authorizes nothing",
        ["UpdateUserCommandHandler.cs"] = "the updated user's grants, for the response; authorizes nothing",
        ["GetUserRolesQueryHandler.cs"] = "the target user's roles, for display; authorizes nothing",
        ["GetUserPermissionsQueryHandler.cs"] = "the target user's direct permissions, for display; authorizes nothing",
        ["AssignRoleCommandHandler.cs"] = "whether the target already holds the role (a duplicate); authorizes nothing",
        ["GrantApplicationAccessCommandHandler.cs"] = "whether the target already holds the role (a re-invitation); authorizes nothing",
        ["RemoveUserRoleCommandHandler.cs"] = "whether the target holds the role it removes; authorizes nothing",
        ["GrantUserPermissionCommandHandler.cs"] = "whether the target already holds the permission (a duplicate); authorizes nothing",
        ["RevokeUserPermissionCommandHandler.cs"] = "whether the target holds the permission it revokes; authorizes nothing",
        // S08 PR B: the reset reads the TARGET's platform permissions to hand them to
        // PermissionGrantGuard (no amplification). The actor's side is that guard's
        // live read, behind the platform users:reset-two-factor gate (pinned below).
        ["ResetUserTwoFactorCommandHandler.cs"] = "the target's grants, compared against the actor's by PermissionGrantGuard; gated by a platform code",
        // X12 PR B: who is TOLD that an account's second factor is hard-locked —
        // the administrators who could reset it. It reads grants to choose
        // recipients, grants nothing, and no token reaches it: an event does.
        ["TwoFactorResetAdministrators.cs"] = "chooses who is told of a hard-locked factor; authorizes nothing (the reset re-checks behind its platform gate)",
    };

    private static IEnumerable<string> GrantReadCalls(string source) =>
        GrantReads.SelectMany(read =>
            Regex.Matches(source, $@"\b{read.Key.Name}\s+(\w+)\s*[,;)=]")
                .Select(declaration => declaration.Groups[1].Value)
                .Distinct()
                .SelectMany(receiver => read.Value
                    .Where(method => Regex.IsMatch(source, $@"\b{Regex.Escape(receiver)}\s*\.\s*{method}\s*\("))
                    .Select(method => $"{read.Key.Name}.{method}")));

    [Fact]
    public void EveryPlatformPermissionReader_IsListedWithItsReason()
    {
        var readers = Sources
            .Where(s => !GrantReadDefinitions.Contains(s.Name))
            .Where(s => GrantReadCalls(s.Source).Any())
            .Select(s => s.Name);

        readers.Should().BeEquivalentTo(PlatformReaders.Keys,
            "a new live reader of a user's roles or permissions must be listed with why a withheld token cannot use it");
    }

    [Fact]
    public void TheScannedReads_AreEveryMethodThatReadsAUsersGrants()
    {
        // A new grant-reading method on one of these interfaces must join the scan:
        // every method about a role or a permission that names a user or asks "has".
        foreach (var (type, scanned) in GrantReads)
        {
            var userGrantReads = type.GetMethods()
                .Select(method => method.Name)
                .Where(name => (name.Contains("Role", StringComparison.Ordinal) || name.Contains("Permission", StringComparison.Ordinal))
                               && (name.Contains("User", StringComparison.Ordinal) || name.StartsWith("Has", StringComparison.Ordinal)))
                .Distinct();

            scanned.Should().BeEquivalentTo(userGrantReads, type.Name);
        }
    }

    /// <summary>The seven callers of PermissionGrantGuard and the platform code that gates each endpoint.</summary>
    private static readonly Dictionary<string, (string Controller, string Command, string Gate)> PermissionGrantGuardCallers = new()
    {
        ["CreateApiKeyCommandHandler.cs"] = ("ApiKeysController.cs", "CreateApiKeyCommand", "PermissionCodes.ApiKeys.Create"),
        ["UpdateApplicationCommandHandler.cs"] = ("ApplicationsController.cs", "UpdateApplicationCommand", "PermissionCodes.Applications.Update"),
        ["CreateRoleCommandHandler.cs"] = ("RolesController.cs", "CreateRoleCommand", "PermissionCodes.Roles.Create"),
        ["GrantRolePermissionCommandHandler.cs"] = ("RolesController.cs", "GrantRolePermissionCommand", "PermissionCodes.Roles.Update"),
        ["AssignRoleCommandHandler.cs"] = ("UsersController.cs", "AssignRoleCommand", "PermissionCodes.Users.ManageRoles"),
        ["GrantUserPermissionCommandHandler.cs"] = ("UsersController.cs", "GrantUserPermissionCommand", "PermissionCodes.Users.ManagePermissions"),
        ["ResetUserTwoFactorCommandHandler.cs"] = ("UsersController.cs", "ResetUserTwoFactorCommand", "PermissionCodes.Users.ResetTwoFactor"),
    };

    /// <summary>
    /// Callers of PermissionGrantGuard that grant nothing and that no request
    /// reaches, each with its one consumer. X12 PR B: the recipients of a
    /// hard-locked factor's notice are the administrators the guard would let
    /// reset it — the reset itself runs the guard again, behind its own gate.
    /// </summary>
    private static readonly Dictionary<string, string> PermissionGrantGuardReadOnlyCallers = new()
    {
        ["TwoFactorResetAdministrators.cs"] = "TwoFactorHardLockAdminNotificationEventHandler.cs",
    };

    [Fact]
    public void PermissionGrantGuardCallers_AllSitBehindAPlatformCode()
    {
        // Injected: the guard is a constructor dependency of each caller.
        var callers = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\bPermissionGrantGuard\s+\w+\s*[,)]"))
            .Select(s => s.Name);

        callers.Should().BeEquivalentTo(PermissionGrantGuardCallers.Keys.Concat(PermissionGrantGuardReadOnlyCallers.Keys));

        foreach (var (caller, consumer) in PermissionGrantGuardReadOnlyCallers)
        {
            var type = Path.GetFileNameWithoutExtension(caller);
            Sources
                .Where(s => s.Name != caller && s.Name != "Program.cs" && Regex.IsMatch(s.Source, $@"\b{type}\b"))
                .Select(s => s.Name)
                .Should().BeEquivalentTo([consumer], $"{caller} grants nothing only while an event handler is its one consumer");
        }

        foreach (var (handler, (controller, command, gate)) in PermissionGrantGuardCallers)
        {
            var source = Source(controller);
            var call = source.IndexOf($"new {command}(", StringComparison.Ordinal);
            call.Should().BePositive($"{controller} sends {command}");

            // The attribute nearest above the action that sends the command.
            var attribute = source.LastIndexOf("[RequirePermission(", call, StringComparison.Ordinal);
            var action = source.LastIndexOf("public async Task<IActionResult>", call, StringComparison.Ordinal);
            attribute.Should().BeLessThan(action, $"{handler}'s endpoint carries its own gate");
            var nextAction = source.LastIndexOf("public async Task<IActionResult>", action - 1, StringComparison.Ordinal);
            attribute.Should().BeGreaterThan(nextAction, $"{handler}'s gate belongs to its own action");
            source[attribute..source.IndexOf(')', attribute)].Should().Contain(gate)
                .And.NotContain("PermissionCodes.Org.", "an org: code would open on org_perm alone");
        }
    }

    [Fact]
    public void OrganizationGrantGuardCallers_PassTheTokensWord()
    {
        var callers = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\bOrganizationGrantGuard\s+\w+\s*[,)]"))
            .Select(s => s.Name);

        callers.Should().BeEquivalentTo(
            ["OrganizationApplicationRoleCheck.cs", "GrantPermissionCommandHandler.cs", "GetAssignableAppRolesQueryHandler.cs"]);
        Source("OrganizationApplicationRoleCheck.cs").Should().Contain("            platformAuthorityInToken,\n");
        Source("GrantPermissionCommandHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");
        Source("GetAssignableAppRolesQueryHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");

        // The application-role check, in turn: the two request paths pass the token's
        // word. Acceptance passes true: the invitation was checked with the token's word
        // when it was sent, the role it names is fixed, and a re-check can only remove
        // that role, never add one (reasoned at ResolveApplicationRoleAsync).
        var roleCheckCallers = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\bOrganizationApplicationRoleCheck\s+\w+\s*[,)]"))
            .Select(s => s.Name);

        roleCheckCallers.Should().BeEquivalentTo(
            ["AssignAppRoleCommandHandler.cs", "InviteMemberCommandHandler.cs", "AcceptInvitationCommandHandler.cs"]);
        Source("AssignAppRoleCommandHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");
        Source("InviteMemberCommandHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");
        Source("AcceptInvitationCommandHandler.cs").Should().Contain("platformAuthorityInToken: true,");

        var controller = Source("OrganizationsController.cs");
        Regex.Matches(controller, @"PlatformAuthorityInToken = HasAnyPermissionClaim\(\)").Should().HaveCount(4,
            "the two grant endpoints, the invitation and the application-role picker set it from the token");
    }
}
