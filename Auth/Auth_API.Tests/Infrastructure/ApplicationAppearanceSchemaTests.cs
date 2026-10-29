using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the application appearance batch: <c>Applications.Theme</c> and
/// <c>Applications.ThemeEnabled</c>, published BEFORE the API that reads them (every application
/// read selects both).
/// <para>
/// Appended last, so the DACPAC publish is a plain <c>ALTER TABLE … ADD</c>. The switch is NOT
/// NULL, so it carries a NAMED default (an unnamed one gets a random name a later publish cannot
/// match), and that default is OFF: every existing application keeps the platform's appearance.
/// The theme holds the same JSON as <c>PlatformSettings.Theme</c>, checked the same way.
/// </para>
/// </summary>
public class ApplicationAppearanceSchemaTests
{
    private static string TableSource() =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                "Applications.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    [Fact]
    public void TheTwoColumns_AreLast_AfterThePreviousBatch()
    {
        var names = OrganizationCreationSchemaTests.DeclaredColumns(TableSource()).Select(c => c.Name).ToList();

        names.Should().HaveCountGreaterThan(3);
        names.TakeLast(3).Should().Equal(["AllowMachineOrganizationsRead", "Theme", "ThemeEnabled"],
            "they are appended, so the publish does not rebuild the table");
    }

    [Fact]
    public void TheTheme_IsNullableJson_WithoutADefault()
    {
        var column = OrganizationCreationSchemaTests.DeclaredColumns(TableSource()).Single(c => c.Name == "Theme");

        column.Definition.Should().MatchRegex(@"^NVARCHAR\(MAX\)\s+NULL$");
        TableSource().Should().MatchRegex(
            @"CONSTRAINT \[CK_Applications_ThemeIsJson\] CHECK \(\[Theme\] IS NULL OR ISJSON\(\[Theme\]\) = 1\)");
    }

    [Fact]
    public void TheSwitch_IsNotNull_WithANamedDefaultOfOff()
    {
        var column = OrganizationCreationSchemaTests.DeclaredColumns(TableSource()).Single(c => c.Name == "ThemeEnabled");

        column.Definition.Should().MatchRegex(
            @"^BIT\s+NOT\s+NULL\s+CONSTRAINT\s+\[DF_Applications_ThemeEnabled\]\s+DEFAULT\s+\(0\)$");
    }

    [Theory]
    [InlineData("Theme")]
    [InlineData("ThemeEnabled")]
    public void Readiness_NamesEachColumn(string column)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(expectation =>
            expectation.Sql.Contains($"COL_LENGTH('dbo.Applications', '{column}') IS NOT NULL")
            && expectation.Name == $"Applications.{column} column");
    }
}
