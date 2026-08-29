using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The permission catalogue and the code that enforces it must describe the same
/// system, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Neither direction was true. Thirty-four enforced codes had no row anywhere on
/// the executed publish path, so no role could be granted them and only
/// super-admin's <c>*</c> reached anything; twenty-one seeded codes reached no
/// gate at all, so granting one looked like an act and was not.
/// </para>
/// <para>
/// Neither failure is visible from inside either half. The API compiles happily
/// against a code no database has ever heard of, the seed inserts happily a code
/// no controller reads, and every existing test passes throughout. The only
/// place the two meet is here.
/// </para>
/// <para>
/// The publish text is composed by following <c>:r</c> from the post-deploy
/// entry point, NOT by globbing SeedData\*.sql. That distinction is the whole
/// point: the defect was a file sitting in that folder, complete and correct and
/// never included, and a glob would have passed on it.
/// </para>
/// </remarks>
public class PermissionSeedCoverageTests
{
    /// <summary>
    /// Seeded rows that are not permission gates and must not be read as one:
    /// the global wildcard, and the area wildcards that exist to be granted
    /// rather than demanded. A wildcard satisfies its leaves by prefix, so it
    /// never appears in a [RequirePermission].
    /// </summary>
    private static bool IsGrantOnly(string code) =>
        code == "*" || code.EndsWith(":*", StringComparison.Ordinal);

    [Fact]
    public void EveryEnforcedPermission_HasASeededRow()
    {
        var missing = PermissionEnforcement.All()
            .Except(SeededCodes(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // A code with no row cannot be granted through any surface: the console
        // picker is filled from the Permissions table, and RolePermissions needs
        // a PermissionId to point at. The endpoint is then reachable only by a
        // holder of "*".
        missing.Should().BeEmpty(
            "every [RequirePermission] code needs a row before anyone but super-admin can be granted it");
    }

    [Fact]
    public void EverySeededPermission_IsEnforcedSomewhere()
    {
        var enforced = PermissionEnforcement.All();

        var unenforced = SeededCodes()
            .Where(code => !IsGrantOnly(code))
            .Where(code => !enforced.Contains(code, StringComparer.OrdinalIgnoreCase))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // The other direction, and the quieter failure. An unenforced code is
        // offered in the console, granted, audited and displayed on the user's
        // permission list - and opens nothing. Retire it in
        // 18_PlatformPermissions.sql rather than leaving it to be granted by
        // someone who reasonably assumes it works.
        unenforced.Should().BeEmpty(
            "a permission the console can grant but no endpoint reads is a promise the system does not keep");
    }

    /// <summary>
    /// Codes that remain ACTIVE once the composed publish text has run.
    /// </summary>
    /// <remarks>
    /// Insertion is not the question - Step 3 inserts the auth: hierarchy on
    /// every fresh publish and Step 14 deactivates it again, and an inactive row
    /// is invisible to every effective-permission query, to the console picker
    /// and to token minting. So the set that matters is inserted-minus-retired.
    /// </remarks>
    private static IReadOnlyCollection<string> SeededCodes()
    {
        var publishText = Inline(PostDeploymentScriptPath());

        var inserted = PermissionCodeInsert.Matches(publishText)
            .Select(match => match.Groups["code"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var retired in RetiredCodes(publishText))
        {
            inserted.Remove(retired);
        }

        return inserted;
    }

    /// <summary>
    /// Codes the publish deactivates: the explicit IN list plus the auth: family
    /// named by the LIKE pattern beside it.
    /// </summary>
    private static IEnumerable<string> RetiredCodes(string publishText)
    {
        var retirement = Regex.Match(
            publishText,
            @"SET\s+\[IsActive\]\s*=\s*0(?<body>.*?);",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!retirement.Success)
        {
            yield break;
        }

        var body = retirement.Groups["body"].Value;

        foreach (System.Text.RegularExpressions.Match literal in Regex.Matches(body, @"N'(?<code>[^']+)'"))
        {
            var code = literal.Groups["code"].Value;
            if (code.EndsWith('%'))
            {
                var prefix = code.TrimEnd('%');
                foreach (System.Text.RegularExpressions.Match inserted in PermissionCodeInsert.Matches(publishText))
                {
                    if (inserted.Groups["code"].Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return inserted.Groups["code"].Value;
                    }
                }
            }
            else
            {
                yield return code;
            }
        }
    }

    /// <summary>
    /// A permission code as the seeds write it: either the guard that precedes a
    /// single INSERT, or a row in the table-variable form 18 uses.
    /// </summary>
    /// <remarks>
    /// The second alternation requires the 2000000 id family, which is what
    /// separates a permission row from a role row. Roles are written in exactly
    /// the same shape under 1000000, so without the anchor this reads "admin"
    /// and "super-admin" as permissions and reports them as unenforced.
    /// </remarks>
    private static readonly Regex PermissionCodeInsert = new(
        @"\[dbo\]\.\[Permissions\]\s+WHERE\s+\[Code\]\s*=\s*N'(?<code>[^']+)'"
        + @"|N'2000000[0-9a-fA-F-]{29}',\s*N'(?<code>[^']+)',\s*N'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Include = new(
        @"^\s*:r\s+(?<path>\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>Inlines every <c>:r</c> include, depth first, as SSDT does.</summary>
    private static string Inline(string path)
    {
        var directory = Path.GetDirectoryName(path)!;

        return Include.Replace(File.ReadAllText(path), match =>
            Inline(Path.GetFullPath(Path.Combine(
                directory,
                match.Groups["path"].Value.Trim('"').Replace('\\', Path.DirectorySeparatorChar)))));
    }

    private static string PostDeploymentScriptPath() => Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql");
}
