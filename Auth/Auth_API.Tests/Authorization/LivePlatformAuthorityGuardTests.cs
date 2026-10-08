using System.Text.RegularExpressions;
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
    private static readonly Dictionary<string, string> PlatformReaders = new()
    {
        ["TokenClaimsResolver.cs"] = "the mint itself: what the policy then decides on",
        ["PermissionGrantGuard.cs"] = "every caller sits behind a platform [RequirePermission] code (pinned below)",
        ["OrganizationGrantGuard.cs"] = "reachable on org_perm alone, so it takes platformAuthorityInToken (pinned below)",
        ["ExternalLoginCommandHandler.cs"] = "the subject's own grants, for a warning on its own link event; authorizes nothing",
        ["CreateUserCommandHandler.cs"] = "the created user's grants, for the response; authorizes nothing",
        ["GetUserByIdQueryHandler.cs"] = "the target user's grants, for display; authorizes nothing",
        ["GetUsersQueryHandler.cs"] = "the listed users' grants, for display; authorizes nothing",
        ["UpdateUserCommandHandler.cs"] = "the updated user's grants, for the response; authorizes nothing",
    };

    [Fact]
    public void EveryPlatformPermissionReader_IsListedWithItsReason()
    {
        var readers = Sources
            .Where(s => !s.Name.StartsWith("IPermissionRepository", StringComparison.Ordinal)
                        && s.Name is not ("PermissionRepository.cs" or "PermissionChecker.cs"))
            .Where(s => Regex.IsMatch(s.Source, @"GetUserEffectivePermissionsAsync\(\s*[\w.]+,\s*cancellationToken\)"))
            .Select(s => s.Name);

        readers.Should().BeEquivalentTo(PlatformReaders.Keys,
            "a new live reader of platform permissions must be listed with why a withheld token cannot use it");
    }

    /// <summary>The six callers of PermissionGrantGuard and the platform code that gates each endpoint.</summary>
    private static readonly Dictionary<string, (string Controller, string Command, string Gate)> PermissionGrantGuardCallers = new()
    {
        ["CreateApiKeyCommandHandler.cs"] = ("ApiKeysController.cs", "CreateApiKeyCommand", "PermissionCodes.ApiKeys.Create"),
        ["UpdateApplicationCommandHandler.cs"] = ("ApplicationsController.cs", "UpdateApplicationCommand", "PermissionCodes.Applications.Update"),
        ["CreateRoleCommandHandler.cs"] = ("RolesController.cs", "CreateRoleCommand", "PermissionCodes.Roles.Create"),
        ["GrantRolePermissionCommandHandler.cs"] = ("RolesController.cs", "GrantRolePermissionCommand", "PermissionCodes.Roles.Update"),
        ["AssignRoleCommandHandler.cs"] = ("UsersController.cs", "AssignRoleCommand", "PermissionCodes.Users.ManageRoles"),
        ["GrantUserPermissionCommandHandler.cs"] = ("UsersController.cs", "GrantUserPermissionCommand", "PermissionCodes.Users.ManagePermissions"),
    };

    [Fact]
    public void PermissionGrantGuardCallers_AllSitBehindAPlatformCode()
    {
        // Injected: the guard is a constructor dependency of each caller.
        var callers = Sources
            .Where(s => Regex.IsMatch(s.Source, @"\bPermissionGrantGuard\s+\w+\s*[,)]"))
            .Select(s => s.Name);

        callers.Should().BeEquivalentTo(PermissionGrantGuardCallers.Keys);

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

        callers.Should().BeEquivalentTo(["AssignAppRoleCommandHandler.cs", "GrantPermissionCommandHandler.cs"]);
        Source("AssignAppRoleCommandHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");
        Source("GrantPermissionCommandHandler.cs").Should().Contain("request.PlatformAuthorityInToken,");

        var controller = Source("OrganizationsController.cs");
        Regex.Matches(controller, @"PlatformAuthorityInToken = HasAnyPermissionClaim\(\)").Should().HaveCount(2,
            "both organization grant endpoints set it from the token");
    }
}
