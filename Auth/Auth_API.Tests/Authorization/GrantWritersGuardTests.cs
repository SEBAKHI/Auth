using System.Text.RegularExpressions;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// OI-223: one writer each for a user's direct grant and for a user's access to an
/// application, counted tree-wide over production sources. A direct grant had two
/// INSERTs (one never called), and the access grant was written inline by the one
/// command that wrote it; now three commands write access, so a second copy of the
/// reinstate-or-create rule would drift from the first without failing anything.
/// </summary>
/// <remarks>
/// Deliberate breaks: an INSERT into UserPermissions in another file; a
/// <c>CreateGrantAsync</c> call from a handler.
/// </remarks>
public class GrantWritersGuardTests
{
    private static readonly List<(string Name, string Source)> Sources = ApiSourceScan.ProductionSources()
        // Line endings normalized: a Windows checkout has CRLF, the repository LF.
        .Select(file => (Path.GetFileName(file.File), file.Source.Replace("\r\n", "\n")))
        .ToList();

    /// <summary>A statement that inserts or merges rows of [dbo].[UserPermissions].</summary>
    private static readonly Regex GrantRowInsert = new(
        @"\b(INSERT(\s+INTO)?|MERGE(\s+INTO)?)\s+(\[?dbo\]?\.)?\[?UserPermissions\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A call that writes an access grant: the repository's four write methods.</summary>
    private static readonly Regex AccessWrite = new(@"\.\s*(CreateGrantAsync|CreateGrantIfAbsentAsync|ReinstateGrantIfNotValidAsync|UpdateGrantAsync)\s*\(");

    private static Dictionary<string, int> CountPerFile(Regex pattern) =>
        Sources.GroupBy(s => s.Name)
            .Select(file => (file.Key, Count: file.Sum(s => pattern.Matches(s.Source).Count)))
            .Where(file => file.Count > 0)
            .ToDictionary(file => file.Key, file => file.Count);

    [Fact]
    public void ExactlyOneStatementInsertsADirectGrant_InUserRepository()
    {
        Sources.Should().NotBeEmpty();

        CountPerFile(GrantRowInsert).Should().BeEquivalentTo(
            new Dictionary<string, int> { ["UserRepository.cs"] = 1 },
            "GrantPermissionAsync's update-or-insert is the one writer of a direct grant");
    }

    /// <summary>A statement that inserts or merges rows of [dbo].[OrganizationUserRoles].</summary>
    private static readonly Regex OrganizationRoleRowInsert = new(
        @"\b(INSERT(\s+INTO)?|MERGE(\s+INTO)?)\s+(\[?dbo\]?\.)?\[?OrganizationUserRoles\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <remarks>
    /// OI-231 T4 (V6): an organization application role is written by the one
    /// update-or-insert that assignment and invitation acceptance share; a plain INSERT
    /// failed with 2627 (500) over an expired, deactivated or leftover row of the key.
    /// The other INSERT is the application provisioning's, which creates or checks the
    /// organization in the same transaction. Deliberate break: restore a plain INSERT
    /// constant for acceptance.
    /// </remarks>
    [Fact]
    public void OrganizationApplicationRoles_AreInsertedOnlyByTheSharedUpdateOrInsert_AndByProvisioning()
    {
        CountPerFile(OrganizationRoleRowInsert).Should().BeEquivalentTo(
            new Dictionary<string, int> { ["OrganizationRepository.cs"] = 2 },
            "UpsertAppRoleSql and ProvisionForApplicationAsync are the only writers that insert a row");

        var repository = Sources.Single(s => s.Name == "OrganizationRepository.cs").Source;
        string Body(string start, string end)
        {
            var from = repository.IndexOf(start, StringComparison.Ordinal);
            from.Should().BeGreaterThanOrEqualTo(0, $"'{start}' must exist");
            return repository[from..repository.IndexOf(end, from, StringComparison.Ordinal)];
        }

        OrganizationRoleRowInsert.Matches(Body("private const string UpsertAppRoleSql", "END;\";")).Should().ContainSingle(
            "the shared update-or-insert inserts only when no row of the key was updated");
        OrganizationRoleRowInsert.Matches(Body("ProvisionForApplicationAsync(", "#endregion")).Should().ContainSingle(
            "provisioning's IF NOT EXISTS insert is the one exception");
    }

    [Fact]
    public void ThePermissionRepository_OffersNoSecondGrantWriter()
    {
        typeof(IPermissionRepository).GetMethod("GrantToUserAsync").Should().BeNull(
            "a second INSERT into UserPermissions skipped the update-or-insert");
    }

    [Fact]
    public void AccessGrants_AreWrittenOnlyByTheSharedWriter()
    {
        var writers = CountPerFile(AccessWrite);

        writers.Should().NotBeEmpty("the scan must find the writer, or it proves nothing");
        writers.Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                ["ApplicationAccessWriter.cs"] = 4,
                // Withdrawing an invitation (UpdateGrantAsync after Revoke): it takes
                // access away, so it gives nothing the grant paths must agree on.
                ["RevokeApplicationAccessCommandHandler.cs"] = 1,
            },
            "the reinstate-or-create rule lives in ApplicationAccessWriter alone");
    }

    /// <summary>
    /// The writer's two write methods, called through an identifier declared as an
    /// <c>ApplicationAccessWriter</c>: found by the receiver's type, so neither a
    /// field's name nor an unrelated <c>WriteAsync</c> (a stream's) counts.
    /// </summary>
    private static int WriterCalls(string source) =>
        Regex.Matches(source, @"\bApplicationAccessWriter\s+(\w+)\s*[,;)=]")
            .Select(declaration => declaration.Groups[1].Value)
            .Distinct()
            .Sum(receiver => Regex.Matches(
                source, $@"\b{Regex.Escape(receiver)}\s*\.\s*(EnsureAsync|WriteAsync)\s*\(").Count);

    /// <remarks>
    /// Deliberate break: call <c>EnsureAsync</c> from a fourth file. A new caller writes
    /// access to an application; it must run the grant guards of the three below first.
    /// </remarks>
    [Fact]
    public void TheAccessWriter_IsCalledByTheThreeGrantPathsOnly()
    {
        var callers = Sources
            .Select(s => (s.Name, Calls: WriterCalls(s.Source)))
            .Where(file => file.Calls > 0)
            .ToDictionary(file => file.Name, file => file.Calls);

        callers.Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                ["GrantApplicationAccessCommandHandler.cs"] = 1,
                ["AssignRoleCommandHandler.cs"] = 1,
                ["GrantUserPermissionCommandHandler.cs"] = 1,
            },
            "each path checks the grant (no amplification, scope, applications:update) before it writes access");
    }

    /// <remarks>
    /// OI-223 T7: the list reads compute canGrant only for a caller, and treat the
    /// Guid.Empty of a token without a user as none. Without the initializer every
    /// picker offers everything again, and every other test stays green: they set
    /// ActorId themselves or mock canGrant. Deliberate break: delete the initializer
    /// in one controller.
    /// </remarks>
    [Theory]
    [InlineData("RolesController.cs", "GetRolesQuery")]
    [InlineData("PermissionsController.cs", "GetPermissionsQuery")]
    public void TheListReads_ComputeWhatTheCallerMayGrant(string controller, string query)
    {
        Sources.Single(s => s.Name == controller).Source.Should().Contain(
            "        var actorId = GetCurrentUserId();\n"
            + $"        var query = new {query}(applicationId, sortBy, sortDirection)\n"
            + "        {\n"
            + "            ActorId = actorId == Guid.Empty ? null : actorId\n"
            + "        };",
            "without the caller the list computes no canGrant, and the console offers every item");
    }
}
