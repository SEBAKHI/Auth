namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Reads the solution's own source text, for the guards that can only be
/// written over it.
/// </summary>
/// <remarks>
/// Several guards in this suite compare a catalogue against what the code
/// actually does, and reflection cannot see the difference: a string passed as
/// an argument, or demanded inside a method body, leaves no metadata behind.
/// Those guards read the source. This is the one place that walks to the
/// solution root and enumerates it, so a guard cannot quietly scan a different
/// tree than its neighbour.
/// </remarks>
internal static class ApiSourceScan
{
    /// <summary>Every non-test C# file under the solution.</summary>
    internal static IEnumerable<(string File, string Source)> ProductionSources()
    {
        var root = SolutionDirectory();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllText(file));
        }
    }

    /// <summary>Every database script of the <c>Auth_DB</c> project, build output excluded.</summary>
    /// <remarks>
    /// Building or publishing the database project writes whole-schema scripts under
    /// <c>bin</c> (a <c>.publish.sql</c> per publish) that repeat every name in the model.
    /// A guard that counted those would pass on a fresh clone and fail on any machine that
    /// had ever published, so the walk leaves build output out here, once, for all of them.
    /// </remarks>
    internal static IEnumerable<string> DatabaseScripts() =>
        Directory.EnumerateFiles(Path.Combine(SolutionDirectory(), "Auth_DB"), "*.sql", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>The directory holding <c>Auth.sln</c>.</summary>
    internal static string SolutionDirectory()
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
