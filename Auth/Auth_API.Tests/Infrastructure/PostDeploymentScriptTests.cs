using System.Text;
using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the composition of the DACPAC post-deployment script.
///
/// SSDT inlines every <c>:r</c> include verbatim and the target server compiles
/// the result one GO-separated batch at a time. An included script that does not
/// end with <c>GO</c> therefore merges into whatever follows it, and two scripts
/// that both open with <c>DECLARE @SystemUserId</c> — the house convention for
/// seed files — collapse into a single batch that fails to compile with
/// "Msg 134: The variable name '@SystemUserId' has already been declared".
/// Because that is a compile-time error the whole batch is skipped, and the
/// publish script runs under <c>:on error exit</c>, so the deployment aborts
/// there: every later seed step silently never runs.
///
/// This happened for real: seed 14 (privacy policy versions) shipped without a
/// terminating GO and stayed harmless until seed 15 was inlined directly after
/// it, which broke every database publish from that commit on. These tests parse
/// the actual script files, so the same class of mistake fails the build instead
/// of a production deployment.
/// </summary>
public class PostDeploymentScriptTests
{
    [Fact]
    public void EveryIncludedScript_EndsWithItsOwnBatchSeparator()
    {
        var offenders = new List<string>();

        foreach (var included in IncludedScripts(PostDeploymentScriptPath()))
        {
            var lastStatementLine = File.ReadAllLines(included)
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

            if (!string.Equals(lastStatementLine, "GO", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetFileName(included));
            }
        }

        offenders.Should().BeEmpty(
            "an included script that does not end with GO merges into the next batch — " +
            "add a trailing GO so each seed/upgrade script is compiled on its own");
    }

    [Fact]
    public void NoBatch_DeclaresTheSameVariableTwice()
    {
        var duplicates = new List<string>();

        foreach (var (batch, index) in ComposeScript(PostDeploymentScriptPath()).Select((b, i) => (b, i)))
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in DeclaredVariable.Matches(batch))
            {
                var name = match.Groups["name"].Value;
                if (!declared.Add(name))
                {
                    duplicates.Add($"batch #{index + 1}: @{name}");
                }
            }
        }

        duplicates.Should().BeEmpty(
            "SQL Server compiles a batch as a unit and rejects a repeated DECLARE with " +
            "Msg 134, which aborts the publish under :on error exit — split the batch " +
            "with GO or drop the redundant declaration");
    }

    [Fact]
    public void ComposedScript_ResolvesEveryInclude()
    {
        var composed = Inline(PostDeploymentScriptPath());

        composed.Should().NotContain(":r ",
            "every :r include must be resolved by the composer — an unresolved one " +
            "means this guard is inspecting less than the deployment actually runs");
        composed.Should().Contain("Post-deployment seed data complete",
            "the composer must reach the end of the post-deployment script");
        IncludedScripts(PostDeploymentScriptPath()).Should().NotBeEmpty(
            "the post-deployment script must inline the seed and upgrade scripts");
    }

    /// <summary>
    /// Matches a declared variable: the name after DECLARE, plus the names after
    /// each comma in a multi-variable declaration. The trailing type keyword keeps
    /// argument lists and VALUES tuples from matching.
    /// </summary>
    private static readonly Regex DeclaredVariable = new(
        @"(?:\bDECLARE\s+|,\s*)@(?<name>\w+)\s+(?:AS\s+)?\[?(?:BIT|TINYINT|SMALLINT|INT|BIGINT|UNIQUEIDENTIFIER|" +
        @"N?VARCHAR|N?CHAR|DATE|DATETIME2?|DATETIMEOFFSET|TIME|DECIMAL|NUMERIC|FLOAT|REAL|MONEY|" +
        @"VARBINARY|BINARY|XML|TABLE|SQL_VARIANT|ROWVERSION|TIMESTAMP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Include = new(
        @"^\s*:r\s+(?<path>\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// Inlines every <c>:r</c> include (depth first, as SSDT does) and returns the
    /// result split into batches on standalone GO lines. Comments and string
    /// literals are neutralized first so neither a GO nor a DECLARE inside them is
    /// mistaken for code.
    /// </summary>
    private static IReadOnlyList<string> ComposeScript(string path)
    {
        var code = StripCommentsAndLiterals(Inline(path));

        return Regex.Split(code, @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static string Inline(string path)
    {
        var directory = Path.GetDirectoryName(path)!;

        return Include.Replace(File.ReadAllText(path), match =>
            Inline(ResolveInclude(directory, match.Groups["path"].Value)));
    }

    /// <summary>Every script reachable from the entry point through <c>:r</c>.</summary>
    private static IReadOnlyList<string> IncludedScripts(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var scripts = new List<string>();

        foreach (System.Text.RegularExpressions.Match match in Include.Matches(File.ReadAllText(path)))
        {
            var included = ResolveInclude(directory, match.Groups["path"].Value);
            scripts.Add(included);
            scripts.AddRange(IncludedScripts(included));
        }

        return scripts;
    }

    private static string ResolveInclude(string includingDirectory, string reference)
    {
        var resolved = Path.GetFullPath(Path.Combine(
            includingDirectory, reference.Trim('"').Replace('\\', Path.DirectorySeparatorChar)));

        File.Exists(resolved).Should().BeTrue($"the post-deployment script includes '{reference}'");
        return resolved;
    }

    /// <summary>
    /// Replaces line comments, block comments and single-quoted literals with
    /// blanks (newlines preserved). A literal-aware pass is required because the
    /// seeded email layouts carry CSS containing <c>/* */</c>, which a naive strip
    /// would treat as a comment and swallow the rest of the file.
    /// </summary>
    private static string StripCommentsAndLiterals(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var index = 0;

        while (index < sql.Length)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (current == '-' && next == '-')
            {
                var lineEnd = sql.IndexOf('\n', index);
                index = Blank(sql, output, index, end: lineEnd < 0 ? sql.Length : lineEnd);
            }
            else if (current == '/' && next == '*')
            {
                var close = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = Blank(sql, output, index, end: close < 0 ? sql.Length : close + 2);
            }
            else if (current == '\'')
            {
                var end = index + 1;
                while (end < sql.Length && !(sql[end] == '\'' && (end + 1 >= sql.Length || sql[end + 1] != '\'')))
                {
                    end += sql[end] == '\'' ? 2 : 1;
                }

                index = Blank(sql, output, index, end: Math.Min(end + 1, sql.Length));
            }
            else
            {
                output.Append(current);
                index++;
            }
        }

        return output.ToString();
    }

    /// <summary>Blanks <paramref name="sql"/> between two offsets, keeping newlines so line structure survives.</summary>
    private static int Blank(string sql, StringBuilder output, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            output.Append(sql[i] == '\n' ? '\n' : ' ');
        }

        return end;
    }

    private static string PostDeploymentScriptPath() => Path.Combine(
        SolutionDirectory(), "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql");

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
