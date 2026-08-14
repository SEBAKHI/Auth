using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the sign-in access rule.
///
/// "May this user sign in to this application?" is answered entirely in SQL, and
/// the repositories are Dapper + raw SQL with no database in the test project —
/// so the SQL text itself is the unit under test, the same approach as
/// <see cref="UserHardDeleteSqlTests"/>.
///
/// The rule is deliberately two branches and no more: the application is open to
/// everyone, or the user holds a valid invitation. Not an organization
/// membership, not an application-scoped role, and not platform administration
/// permissions. Every extra branch anyone is tempted to add here widens who can
/// sign in to a restricted application, which is the whole point of the mode.
/// </summary>
public class ApplicationEntitlementSqlTests
{
    [Fact]
    public void ThePredicate_RequiresTheApplicationToBeLiveAndSwitchedOn()
    {
        var predicate = EntitlementPredicate();

        predicate.Should().MatchRegex(@"a\.\[IsDeleted\]\s*=\s*0",
            "a soft-deleted application admits nobody");
        predicate.Should().MatchRegex(@"a\.\[IsActive\]\s*=\s*1",
            "the on/off switch beats the access mode: an application switched off admits nobody");
    }

    [Fact]
    public void ThePredicate_AdmitsEveryoneOnlyInOpenMode()
    {
        EntitlementPredicate().Should().MatchRegex(@"a\.\[AccessMode\]\s*=\s*@Everyone",
            "an application open to everyone admits any authenticated user without consulting the access list");
    }

    [Theory]
    [InlineData(@"aua\.\[IsActive\]\s*=\s*1", "a deactivated invitation admits nobody")]
    [InlineData(@"aua\.\[RevokedAt\]\s+IS\s+NULL", "a withdrawn invitation admits nobody")]
    [InlineData(@"aua\.\[ExpiresAt\]\s+IS\s+NULL\s+OR\s+aua\.\[ExpiresAt\]\s*>\s*GETUTCDATE\(\)",
        "a lapsed invitation admits nobody — a trial given an expiry must end on its own")]
    [InlineData(@"aua\.\[UserId\]\s*=\s*@UserId", "the invitation must belong to the user signing in")]
    [InlineData(@"aua\.\[ApplicationId\]\s*=\s*a\.\[Id\]", "the invitation must be for the application being entered")]
    public void ThePredicate_RequiresAValidInvitationInRestrictedMode(string pattern, string because)
    {
        new Regex(pattern, RegexOptions.IgnoreCase).IsMatch(EntitlementPredicate())
            .Should().BeTrue(because);
    }

    [Theory]
    [InlineData("OrganizationUsers")]
    [InlineData("OrganizationApplications")]
    [InlineData("OrganizationUserRoles")]
    [InlineData("OrganizationUserPermissions")]
    [InlineData("UserRoles")]
    [InlineData("UserPermissions")]
    public void ThePredicate_AdmitsNobodyThroughAnyOtherTable(string table)
    {
        EntitlementPredicate().Should().NotContain(table,
            $"the access rule is open-mode or invitation, full stop; reading {table} here would let " +
            "someone into a restricted application without an invitation, and a restricted application " +
            "cannot have organizations in the first place");
    }

    [Fact]
    public void TheGateAndTheListing_ShareOneDefinition()
    {
        var repository = ReadRepository();

        // Both the yes/no gate and the "which applications may I use" listing
        // interpolate the same const. Two hand-written copies of an access rule
        // drift, and the copy that drifts is the one nobody is reading.
        Regex.Matches(repository, @"\{EntitlementPredicateSql\}").Count
            .Should().BeGreaterThanOrEqualTo(2,
                "IsUserEntitledAsync and GetApplicationsForUserAsync must both be built from the same predicate");
    }

    [Fact]
    public void NoOtherRepository_AnswersTheSameQuestion()
    {
        // HasAppAccessAsync used to answer a near-identical question from the
        // organization repository and was dead code. It was removed rather than
        // left in place: a second definition of an access rule is a trap, and a
        // dead one with now-wrong semantics is a worse trap.
        var organizationRepository = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "OrganizationRepository.cs"));

        organizationRepository.Should().NotContain("HasAppAccessAsync",
            "the sign-in access rule lives in ApplicationAccessRepository alone");
    }

    /// <summary>
    /// Extracts the shared predicate constant — the actual access rule.
    /// </summary>
    private static string EntitlementPredicate()
    {
        var repository = ReadRepository();

        var start = repository.IndexOf("EntitlementPredicateSql = @\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1,
            "ApplicationAccessRepository must define the access rule as a single named constant");

        var end = repository.IndexOf("\";", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the predicate constant must be terminated");

        return repository[start..end];
    }

    private static string ReadRepository() => File.ReadAllText(Path.Combine(
        SolutionDirectory(), "Auth.Infrastructure", "Persistence", "ApplicationAccessRepository.cs"));

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
