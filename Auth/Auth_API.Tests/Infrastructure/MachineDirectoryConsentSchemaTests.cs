using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of OI-202's schema batch: <c>Applications.AllowMachineOrganizationsRead</c>,
/// published BEFORE the API that reads it (every application read selects it).
/// <para>
/// Appended last, so the DACPAC publish is a plain <c>ALTER TABLE … ADD</c>. NOT NULL, so it
/// carries a NAMED default (an unnamed one gets a random name a later publish cannot match), and
/// that default is ON: every existing application reads consented after the publish (owner
/// decision D-202-2).
/// </para>
/// </summary>
public class MachineDirectoryConsentSchemaTests
{
    private static string TableSource() =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                "Applications.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    [Fact]
    public void TheColumn_IsLast_AfterThePreviousBatch()
    {
        var names = OrganizationCreationSchemaTests.DeclaredColumns(TableSource()).Select(c => c.Name).ToList();

        // Only a later batch follows it: the application's appearance (ApplicationAppearanceSchemaTests).
        names.Should().HaveCountGreaterThan(4);
        names.TakeLast(4).Should().Equal(["InitiateLoginUri", "AllowMachineOrganizationsRead", "Theme", "ThemeEnabled"],
            "it is appended, so the publish does not rebuild the table");
    }

    [Fact]
    public void TheSwitch_IsNotNull_WithANamedDefaultOfOn()
    {
        var column = OrganizationCreationSchemaTests.DeclaredColumns(TableSource())
            .Single(c => c.Name == "AllowMachineOrganizationsRead");

        column.Definition.Should().MatchRegex(
            @"^BIT\s+NOT\s+NULL\s+CONSTRAINT\s+\[DF_Applications_AllowMachineOrganizationsRead\]\s+DEFAULT\s+\(1\)$");
    }

    [Fact]
    public void Readiness_NamesTheColumn()
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(expectation =>
            expectation.Sql.Contains("COL_LENGTH('dbo.Applications', 'AllowMachineOrganizationsRead') IS NOT NULL")
            && expectation.Name.Contains("Applications.AllowMachineOrganizationsRead"));
    }
}
