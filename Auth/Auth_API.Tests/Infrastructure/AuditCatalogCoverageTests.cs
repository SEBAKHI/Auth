using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The audit catalogue and the code that writes audit rows must describe the
/// same system, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Reflection cannot see this. An audit row's two identifying fields are plain
/// strings passed as named arguments, so the compiler is equally happy with a
/// catalogue constant, a typo, and an action filed under a category it does not
/// belong to. The only place the call sites and
/// <see cref="AuditActions.ByCode"/> meet is here, and the meeting has to happen
/// over the source text.
/// </para>
/// <para>
/// Four of the codes are chosen by a conditional rather than written as a
/// literal, so a check that reads only the first argument value would pass over
/// <c>user.logout.all</c> and <c>application.deactivated</c> without noticing.
/// Every <c>AuditActions.</c> reference inside a call's argument list is
/// collected, not just the first.
/// </para>
/// </remarks>
public class AuditCatalogCoverageTests
{
    /// <summary>
    /// Only these two factories write an audit row. Scoping to them matters:
    /// <c>LoginAttempt.CreateFailure</c> exists and has nothing to do with the
    /// catalogue, and a looser match would demand catalogue constants from it.
    /// </summary>
    private static readonly Regex CallStart =
        new(@"AuditLog\.Create(Success|Failure)\s*\(", RegexOptions.Compiled);

    private static readonly Regex ActionTypeArgument =
        new(@"actionType:\s*(?<value>[^,]+?)\s*,", RegexOptions.Compiled);

    private static readonly Regex ActionConstant =
        new(@"AuditActions\.(?<name>[A-Za-z0-9_]+)", RegexOptions.Compiled);

    private static readonly Regex ActionTypeConstant =
        new(@"^AuditActionTypes\.(?<name>[A-Za-z0-9_]+)$", RegexOptions.Compiled);

    /// <summary>A single place an audit row is written, as source text.</summary>
    private sealed record CallSite(string File, int Line, string Arguments);

    [Fact]
    public void TheScan_FindsTheCallSitesAtAll()
    {
        var sites = ProductionSources()
            .SelectMany(source => CallSitesIn(source.File, source.Source))
            .ToList();

        // Guards the regex and the directory walk, not the code under test. Every
        // other assertion in this file iterates these sites: if the scan ever
        // returns nothing - a moved folder, a renamed factory, a changed call
        // shape - all of them would pass while checking nothing at all.
        sites.Should().HaveCountGreaterThan(40,
            "the audit call sites must be found before anything can be asserted about them");
        sites.Select(site => site.File).Distinct()
            .Should().HaveCountGreaterThan(40, "they live one per handler");
    }

    [Fact]
    public void NoCallSite_StillPassesALiteral()
    {
        var offenders = new List<string>();

        foreach (var (file, source) in ProductionSources())
        {
            foreach (var site in CallSitesIn(file, source))
            {
                if (site.Arguments.Contains("actionType: \"", StringComparison.Ordinal)
                    || site.Arguments.Contains("action: \"", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(site.File)}:{site.Line}");
                }
            }
        }

        // A literal here is not a style problem. It is how an action came to be
        // filed under one category in the handler that wrote it and another in
        // the handler beside it, with nothing able to say so.
        offenders.Should().BeEmpty(
            "every audit call site must name its action and category through AuditActions/AuditActionTypes");
    }

    [Fact]
    public void EveryCallSite_FilesItsActionUnderTheCategoryTheCatalogueGivesIt()
    {
        var mismatches = new List<string>();

        foreach (var (file, source) in ProductionSources())
        {
            foreach (var site in CallSitesIn(file, source))
            {
                var typeMatch = ActionTypeArgument.Match(site.Arguments);
                typeMatch.Success.Should().BeTrue(
                    $"{Path.GetFileName(site.File)}:{site.Line} writes an audit row without an actionType");

                var typeConstant = ActionTypeConstant.Match(typeMatch.Groups["value"].Value.Trim());
                typeConstant.Success.Should().BeTrue(
                    $"{Path.GetFileName(site.File)}:{site.Line} does not take its category from AuditActionTypes");

                var categoryName = typeConstant.Groups["name"].Value;
                var category = ConstantValue(typeof(AuditActionTypes), categoryName);
                category.Should().NotBeNull(
                    $"AuditActionTypes.{categoryName} does not exist");

                foreach (System.Text.RegularExpressions.Match action in ActionConstant.Matches(site.Arguments))
                {
                    var actionName = action.Groups["name"].Value;
                    var code = ConstantValue(typeof(AuditActions), actionName);
                    if (code is null) continue; // ByCode / All, not an action constant

                    if (!AuditActions.ByCode.TryGetValue(code, out var expected))
                    {
                        mismatches.Add($"{Path.GetFileName(site.File)}:{site.Line} writes '{code}', absent from ByCode");
                    }
                    else if (expected != category)
                    {
                        mismatches.Add(
                            $"{Path.GetFileName(site.File)}:{site.Line} files '{code}' under '{category}', " +
                            $"the catalogue says '{expected}'");
                    }
                }
            }
        }

        mismatches.Should().BeEmpty(
            "an action's category must be the same wherever it is written, or a filtered audit view lies by omission");
    }

    [Fact]
    public void EveryCatalogueEntry_IsActuallyWrittenSomewhere()
    {
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (file, source) in ProductionSources())
        {
            foreach (var site in CallSitesIn(file, source))
            {
                foreach (System.Text.RegularExpressions.Match action in ActionConstant.Matches(site.Arguments))
                {
                    var code = ConstantValue(typeof(AuditActions), action.Groups["name"].Value);
                    if (code is not null) written.Add(code);
                }
            }
        }

        var dead = AuditActions.All
            .Where(code => !written.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // The console translates this catalogue into seven languages and lists
        // it as what the system records. An entry nothing writes is a promise of
        // an event that never arrives.
        dead.Should().BeEmpty(
            "a catalogued action that no call site writes is an event the console advertises and never shows");
    }

    [Fact]
    public void EveryCatalogueEntry_HasACategoryFromTheClosedList()
    {
        AuditActions.ByCode.Values.Distinct()
            .Should().BeSubsetOf(AuditActionTypes.All,
                "ByCode must not invent a category the console has no name for");
    }

    /// <summary>Resolves a <c>const string</c> by name, or null when it is not one.</summary>
    private static string? ConstantValue(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field is { IsLiteral: true } ? field.GetRawConstantValue() as string : null;
    }

    /// <summary>Every non-test C# file under the solution.</summary>
    private static IEnumerable<(string File, string Source)> ProductionSources() =>
        ApiSourceScan.ProductionSources();

    /// <summary>
    /// The argument list of every audit-row factory call in one file.
    /// </summary>
    /// <remarks>
    /// Parenthesis-balanced rather than "up to the next <c>);</c>": these calls
    /// nest calls and carry interpolated strings, and a naive stop lands in the
    /// middle of one. String literals are skipped so that a bracket inside
    /// <c>additionalData</c> cannot unbalance the count.
    /// </remarks>
    private static IEnumerable<CallSite> CallSitesIn(string file, string source)
    {
        foreach (System.Text.RegularExpressions.Match start in CallStart.Matches(source))
        {
            var open = start.Index + start.Length; // just past the '('
            var depth = 1;
            var inString = false;
            var i = open;

            for (; i < source.Length && depth > 0; i++)
            {
                var c = source[i];

                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '(') depth++;
                else if (c == ')') depth--;
            }

            if (depth != 0) continue; // unbalanced: not something to judge

            var line = source.Take(start.Index).Count(c => c == '\n') + 1;
            yield return new CallSite(file, line, source[open..(i - 1)]);
        }
    }

}
