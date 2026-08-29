using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// <see cref="PermissionCodes"/> and the codes the API demands must name the
/// same set, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue exists to be read by something that cannot run C#: the
/// console's mirror is held against this one file as text. That only helps if
/// the file is true, and nothing in the compiler makes it so - a constant here
/// is just a string, and an endpoint demanding a code that never reached the
/// catalogue compiles exactly as well as one that did.
/// </para>
/// <para>
/// Both directions matter and they fail differently. A demanded code missing
/// from the catalogue silently drops out of the console mirror's contract, so
/// the drift this catalogue was built to stop walks straight back in. A
/// catalogued code that no endpoint demands is the quieter one: it reaches the
/// console, gets gated on, and hides a control for a permission the API never
/// asks for.
/// </para>
/// <para>
/// This is not the SQL-seed guard. <see cref="PermissionSeedCoverageTests"/>
/// holds the demanded codes against the rows the publish leaves active, and
/// uses the word "catalogue" for that seed. Two meanings in one file is how a
/// reader misreads which half of the system a failure is accusing.
/// </para>
/// </remarks>
public class PermissionCatalogueCoverageTests
{
    /// <summary>A constant as the catalogue declares it, read from the file.</summary>
    private static readonly Regex DeclaredConstant =
        new(@"public const string\s+\w+\s*=\s*""(?<code>[^""]+)""\s*;", RegexOptions.Compiled);

    [Fact]
    public void TheScan_FindsTheControllersAndTheCatalogueAtAll()
    {
        // Guards every assertion below. Each one is an Except over two lists,
        // and two empty lists agree perfectly: a reflection scan that stopped
        // finding controllers, or a catalogue that stopped being read, would
        // turn this whole file green while proving nothing.
        PermissionEnforcement.Controllers().Should().HaveCountGreaterThan(20,
            "the attribute scan covers one assembly, and losing it loses both sides of the comparison at once");

        PermissionCodes.All.Should().HaveCountGreaterThan(45,
            "the catalogue is read by reflection over nested public classes, which a modifier change can silently empty");

        PermissionEnforcement.FromCode().Should().NotBeEmpty(
            "codes demanded in controller bodies are read from source text, and a moved call site would empty the scan");
    }

    [Fact]
    public void EveryEnforcedPermission_IsInTheCatalogue()
    {
        var missing = PermissionEnforcement.All()
            .Except(PermissionCodes.All, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "the console mirror is checked against PermissionCodes alone, so a demanded code missing from it "
            + "is a code the mirror is never asked about");
    }

    [Fact]
    public void EveryCataloguedPermission_IsEnforced()
    {
        var enforced = PermissionEnforcement.All();

        var unenforced = PermissionCodes.All
            .Where(code => !enforced.Contains(code, StringComparer.OrdinalIgnoreCase))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        unenforced.Should().BeEmpty(
            "a catalogued code no endpoint demands still reaches the console, where gating on it hides a control "
            + "for a permission the API never asks for");
    }

    [Fact]
    public void NoCode_IsDeclaredTwice()
    {
        var duplicates = PermissionCodes.All
            .GroupBy(code => code, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // Two names for one code make the console mirror's key-path comparison
        // ambiguous: both paths are legitimate, and which one the mirror kept
        // is then a coin toss nobody documented.
        duplicates.Should().BeEmpty("one code, one constant");
    }

    [Fact]
    public void EveryCode_IsLowercase()
    {
        var mixed = PermissionCodes.All
            .Where(code => !string.Equals(code, code.ToLowerInvariant(), StringComparison.Ordinal))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // Matching is case-insensitive on this side and case-sensitive on the
        // console's, and the enforced set is a case-insensitive hash set that
        // keeps whichever casing reflection happened to yield first - an order
        // no runtime guarantees. A capital letter here is therefore an
        // intermittent frontend failure, which is the worst kind to inherit.
        mixed.Should().BeEmpty("permission codes are lowercase everywhere they are written");
    }

    [Fact]
    public void All_ListsEveryConstantTheFileDeclares()
    {
        var declared = DeclaredConstant
            .Matches(CatalogueSource())
            .Select(match => match.Groups["code"].Value)
            .ToList();

        declared.Should().HaveCountGreaterThan(45, "the file is read as text, and a changed shape would empty this");

        // Reflection reads only PUBLIC nested types. A group class that loses
        // its modifier - nested types default to private - drops every code it
        // holds out of All, and the direction tests would then report those
        // codes as "demanded but uncatalogued" while the file plainly declares
        // them. This says which of the two readings went wrong.
        declared.Except(PermissionCodes.All, StringComparer.Ordinal).Should().BeEmpty(
            "PermissionCodes.All must list every constant the file declares; a group class that is not public is invisible to it");
    }

    private static string CatalogueSource() => File.ReadAllText(Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth.Domain", "Constants", "PermissionCodes.cs"));
}
