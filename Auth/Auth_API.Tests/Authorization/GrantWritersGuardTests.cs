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

    /// <summary>A call that writes an access grant: the repository's two write methods.</summary>
    private static readonly Regex AccessWrite = new(@"\.\s*(CreateGrantAsync|UpdateGrantAsync)\s*\(");

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
                ["ApplicationAccessWriter.cs"] = 2,
                // Withdrawing an invitation (UpdateGrantAsync after Revoke): it takes
                // access away, so it gives nothing the grant paths must agree on.
                ["RevokeApplicationAccessCommandHandler.cs"] = 1,
            },
            "the reinstate-or-create rule lives in ApplicationAccessWriter alone");
    }
}
