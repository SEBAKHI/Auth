namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards what an organization membership is allowed to be worth.
///
/// A membership is a row in OrganizationUsers pointing at a role. The write paths check only
/// that the role does not belong to an application, which is a negative test and does not
/// exclude a platform role: super-admin is ApplicationId NULL, so it satisfied the check and
/// could be bound as a membership role. Its wildcard permission then came back from these
/// queries and was minted into an org_perm claim, and the authorization handler treats the
/// wildcard as matching every requirement.
///
/// The rule is enforced here, on the read, rather than on the write, for two reasons: it also
/// neutralises a row that is already bound that way, because authority is re-derived from
/// these queries on every request; and it cannot be reached around by a fourth write path.
///
/// The organization repository holds two different families of query and only one of them is
/// in scope. Membership queries join RolePermissions to OrganizationUsers.RoleId and must
/// carry the filter. Application-scoped queries join it to OrganizationUserRoles and must NOT:
/// they feed OrganizationGrantGuard and the SDK-facing permission resolver, and narrowing them
/// to org: codes would blind the guard to the actor's application permissions and strip every
/// relying party of its organization-derived authority. These tests hold both halves of that
/// line, so an edit that looks like a consistency improvement fails with the reason attached.
/// </summary>
public class OrganizationMembershipScopeSqlTests
{
    private const string MembershipJoin = "rp.[RoleId] = ou.[RoleId]";
    private const string ApplicationScopedJoin = "our.[RoleId] = rp.[RoleId]";
    private const string OrgCodeFilter = "p.[Code] LIKE 'org:%'";

    [Fact]
    public void EveryMembershipQuery_YieldsOrganizationCodesOnly()
    {
        var queries = QueriesJoiningOn(MembershipJoin);

        queries.Should().HaveCount(3,
            "there are three membership permission projections; a fourth needs the same filter, "
            + "and this count is what forces someone adding one to come here");

        queries.Should().OnlyContain(query => query.Contains(OrgCodeFilter, StringComparison.Ordinal),
            "a membership must yield organization authority and nothing else — without this filter, "
            + "binding a platform role as a membership role mints an org_perm claim carrying its wildcard");
    }

    [Fact]
    public void ApplicationScopedQueries_AreDeliberatelyNotFiltered()
    {
        var queries = QueriesJoiningOn(ApplicationScopedJoin);

        queries.Should().NotBeEmpty("the application-scoped projections still exist");

        queries.Should().OnlyContain(query => !query.Contains(OrgCodeFilter, StringComparison.Ordinal),
            "these answer what a member may do inside an APPLICATION, and they feed OrganizationGrantGuard "
            + "and PermissionChecker — filtering them to org: codes would make the guard reject every "
            + "legitimate application-permission grant and would strip relying parties of their authority");
    }

    /// <summary>
    /// Returns the text from each occurrence of <paramref name="join"/> to the end of its SQL
    /// literal, which is where that query's WHERE clause lives.
    /// </summary>
    private static IReadOnlyList<string> QueriesJoiningOn(string join)
    {
        var repository = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "OrganizationRepository.cs"));

        var queries = new List<string>();
        var index = 0;

        while ((index = repository.IndexOf(join, index, StringComparison.Ordinal)) >= 0)
        {
            queries.Add(repository[index..EndOfVerbatimLiteral(repository, index)]);
            index += join.Length;
        }

        return queries;
    }

    /// <summary>
    /// Finds the quote that closes the verbatim string the scan is inside. A verbatim string
    /// escapes a quote by doubling it, so the closing one is the first quote not paired with
    /// another. Getting this boundary wrong is not a cosmetic bug: a window that overruns into
    /// the next query makes both tests pass no matter what this file says, which is how a guard
    /// becomes decoration.
    /// </summary>
    private static int EndOfVerbatimLiteral(string source, int from)
    {
        for (var i = from; i < source.Length; i++)
        {
            if (source[i] != '"')
            {
                continue;
            }

            if (i + 1 < source.Length && source[i + 1] == '"')
            {
                i++;
                continue;
            }

            return i;
        }

        return source.Length;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
