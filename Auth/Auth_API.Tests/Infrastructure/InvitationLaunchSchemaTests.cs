using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of OI-192's schema batch: <c>Applications.InitiateLoginUri</c> and
/// <c>OrganizationInvitations.ApplicationRoleOutcome</c>, published BEFORE the API that
/// reads them (every application read and every invitation read selects them).
/// <para>
/// Both are appended last and nullable with no default, so the DACPAC publish is a plain
/// <c>ALTER TABLE … ADD</c>; the outcome's two CHECKs are named, so a later publish can
/// match them instead of dropping and re-creating a randomly named constraint.
/// </para>
/// </summary>
public class InvitationLaunchSchemaTests
{
    private static string TableSource(string table) =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                $"{table}.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    /// <summary>
    /// Columns a LATER batch appended after this one, in their order. Only these may follow it;
    /// each later batch pins its own shape in its own guard (OI-202:
    /// <see cref="MachineDirectoryConsentSchemaTests"/>).
    /// </summary>
    private static readonly Dictionary<string, string[]> LaterBatchColumns = new()
    {
        ["Applications"] = ["AllowMachineOrganizationsRead"],
    };

    [Theory]
    [InlineData("Applications", "LogoUrlDark", "InitiateLoginUri", @"^NVARCHAR\(500\)\s+NULL$")]
    [InlineData("OrganizationInvitations", "ApplicationRoleId", "ApplicationRoleOutcome", @"^NVARCHAR\(16\)\s+NULL$")]
    public void TheColumn_IsLast_AfterThePreviousBatch_AndNullableWithoutADefault(
        string table, string previousColumn, string column, string definition)
    {
        var columns = OrganizationCreationSchemaTests.DeclaredColumns(TableSource(table), table);
        var later = LaterBatchColumns.GetValueOrDefault(table, []);

        columns.Should().HaveCountGreaterThan(2);
        columns.Select(c => c.Name).TakeLast(2 + later.Length).Should().Equal([previousColumn, column, .. later],
            "it is appended, so the publish does not rebuild the table, and only a later batch follows it");
        columns.Single(c => c.Name == column).Definition.Should().MatchRegex(definition);
    }

    [Fact]
    public void TheOutcome_IsCheckedUnderNamedConstraints()
    {
        var source = TableSource("OrganizationInvitations");

        source.Should().MatchRegex(
            @"CONSTRAINT \[CK_OrganizationInvitations_ApplicationRoleOutcome\] CHECK \(\s*\[ApplicationRoleOutcome\] IS NULL OR \[ApplicationRoleOutcome\] IN \('Applied', 'AlreadyHeld', 'Dropped'\)\)",
            "only the three names the domain stores");
        source.Should().MatchRegex(
            @"CONSTRAINT \[CK_OrganizationInvitations_ApplicationRoleOutcomeNeedsRole\] CHECK \(\s*\[ApplicationRoleOutcome\] IS NULL OR \[ApplicationId\] IS NOT NULL\)",
            "an outcome exists only for an invitation that named an application role");
    }

    [Fact]
    public void TheStoredNames_AreTheEnumsMembers()
    {
        // The repository writes the member's name and parses it back: a renamed member
        // would be refused by the CHECK above, or fail to parse a stored row.
        Enum.GetNames<Auth.Domain.Enums.InvitationApplicationRoleOutcome>()
            .Should().BeEquivalentTo(["Applied", "AlreadyHeld", "Dropped"]);
    }
}
