using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the branding batch: <c>Applications.LogoUrlDark</c> and
/// <c>PlatformSettings.Theme</c>, published BEFORE the API that reads them.
/// <para>
/// Both are appended last and nullable with no default, so the DACPAC publish is
/// a plain <c>ALTER TABLE … ADD</c>: DacFx honours declared order, and a column
/// anywhere else rebuilds the table.
/// </para>
/// </summary>
public class BrandingSchemaTests
{
    private static string TableSource(string table) =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                $"{table}.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    /// <summary>
    /// Columns a LATER batch appended after this one, in their order. Only these may
    /// follow it; each later batch pins its own shape in its own guard (OI-192:
    /// <see cref="InvitationLaunchSchemaTests"/>).
    /// </summary>
    private static readonly Dictionary<string, string[]> LaterBatchColumns = new()
    {
        // OI-202: MachineDirectoryConsentSchemaTests.
        ["Applications"] = ["InitiateLoginUri", "AllowMachineOrganizationsRead"],
    };

    [Theory]
    [InlineData("Applications", "LogoUrlDark", @"^NVARCHAR\(500\)\s+NULL$")]
    [InlineData("PlatformSettings", "Theme", @"^NVARCHAR\(1000\)\s+NULL$")]
    public void TheColumn_IsLast_AndNullableWithoutADefault(string table, string column, string definition)
    {
        var columns = OrganizationCreationSchemaTests.DeclaredColumns(TableSource(table), table);
        var later = LaterBatchColumns.GetValueOrDefault(table, []);

        columns.Should().NotBeEmpty();
        columns.Select(c => c.Name).TakeLast(1 + later.Length).Should().Equal([column, .. later],
            "it is appended, so the publish does not rebuild the table, and only a later batch follows it");
        columns.Single(c => c.Name == column).Definition.Should().MatchRegex(definition);
    }

    [Fact]
    public void TheTheme_IsCheckedToBeJson_UnderANamedConstraint()
    {
        TableSource("PlatformSettings").Should().MatchRegex(
            @"CONSTRAINT \[CK_PlatformSettings_ThemeIsJson\] CHECK \(\[Theme\] IS NULL OR ISJSON\(\[Theme\]\) = 1\)");
    }
}
