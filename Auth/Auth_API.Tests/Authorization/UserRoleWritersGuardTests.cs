using System.Text.RegularExpressions;
using Auth.Application.Features.Applications.GrantApplicationAccess;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// OI-109 T4: a user's role is authority, so every place that writes one must run
/// the grant guards or stay inside one application. Creating a user once wrote
/// platform roles with neither (<c>users:create</c> alone made a super-administrator);
/// this guard lists every writer with why it is safe, and a new one fails it until
/// it is listed.
/// </summary>
public class UserRoleWritersGuardTests
{
    private static readonly List<(string Name, string Source)> Sources = ApiSourceScan.ProductionSources()
        // Line endings normalized: a Windows checkout has CRLF, the repository LF.
        .Select(file => (Path.GetFileName(file.File), file.Source.Replace("\r\n", "\n")))
        .ToList();

    private static string Source(string name) => Sources.Single(s => s.Name == name).Source;

    /// <summary>
    /// Building a user-role row, or handing one to a repository method that stores it.
    /// <c>\bUserRole</c> leaves out <c>OrganizationUserRole</c>, a different table.
    /// </summary>
    private static readonly Regex RoleWrite = new(
        @"\bUserRole\.Create\s*\(|\.\s*AssignToUserAsync\s*\(|\.\s*AssignRoleAsync\s*\(");

    /// <summary>A statement that inserts, updates or merges rows of [dbo].[UserRoles].</summary>
    private static readonly Regex RoleRowStatement = new(
        @"\b(INSERT(\s+INTO)?|UPDATE|MERGE(\s+INTO)?)\s+(\[?dbo\]?\.)?\[?UserRoles\b",
        RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> RoleWriters = new()
    {
        ["AssignRoleCommandHandler.cs"] =
            "POST users/{id}/roles: PermissionGrantGuard (no amplification) and PlatformGrantFactorGuard run before the write (pinned below)",
        ["GrantApplicationAccessCommandHandler.cs"] =
            "the invitation's role is scoped to its own application, a non-nullable id, so it never writes platform scope (pinned below)",
    };

    private static readonly Dictionary<string, string> RoleRowStatements = new()
    {
        ["RoleRepository.cs"] = "AssignToUserAsync: what the two writers above call",
        ["UserRepository.cs"] = "AssignRoleAsync: no production caller; a new caller fails the writer list above",
    };

    [Fact]
    public void EveryWriterOfAUsersRole_IsListedWithItsReason()
    {
        Sources.Should().NotBeEmpty();

        var writers = Sources.Where(s => RoleWrite.IsMatch(s.Source)).Select(s => s.Name).ToList();

        writers.Should().NotBeEmpty("the scan must find the known writers, or it proves nothing");
        writers.Should().BeEquivalentTo(RoleWriters.Keys,
            "a new writer of a user's role must run the grant guards or stay inside one application, and be listed here");
    }

    [Fact]
    public void EveryStatementOnUserRoleRows_IsInARepositoryListedHere()
    {
        var files = Sources.Where(s => RoleRowStatement.IsMatch(s.Source)).Select(s => s.Name).ToList();

        files.Should().NotBeEmpty("the scan must find the two repository definitions, or it proves nothing");
        files.Should().BeEquivalentTo(RoleRowStatements.Keys,
            "SQL that stores a user's role outside these methods bypasses the writer list above");
    }

    [Fact]
    public void AssignRole_RunsBothGrantGuardsBeforeItWrites()
    {
        var handler = Source("AssignRoleCommandHandler.cs");
        var write = handler.IndexOf(".AssignToUserAsync(", StringComparison.Ordinal);
        write.Should().BePositive();

        foreach (var guard in new[] { "PermissionGrantGuard", "PlatformGrantFactorGuard" })
        {
            // Injected, then called through the injected field before the row is stored.
            var fields = Regex.Matches(handler, $@"\b{guard}\s+(\w+)\s*[,;)]")
                .Select(declaration => declaration.Groups[1].Value)
                .ToList();
            fields.Should().NotBeEmpty($"{guard} is a constructor dependency");

            var firstCall = fields
                .Select(field => Regex.Match(handler, $@"\b{Regex.Escape(field)}\s*\.\s*\w+\s*\("))
                .Where(call => call.Success)
                .Select(call => call.Index)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            firstCall.Should().BeLessThan(write, $"{guard} decides before the role is written");
        }
    }

    [Fact]
    public void GrantApplicationAccess_WritesTheRoleAtItsApplicationOnly()
    {
        typeof(GrantApplicationAccessCommand).GetProperty(nameof(GrantApplicationAccessCommand.ApplicationId))!
            .PropertyType.Should().Be(typeof(Guid), "a nullable id could be null, which is platform scope");

        var handler = Source("GrantApplicationAccessCommandHandler.cs");
        var create = Regex.Match(handler, @"\bUserRole\.Create\s*\((?<arguments>[^;]*)\);");
        create.Success.Should().BeTrue();
        create.Groups["arguments"].Value.Should().Contain("applicationId: request.ApplicationId",
            "the invitation's role belongs to the application it invites to");
    }
}
