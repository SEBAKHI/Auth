namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// Where the machine scheme may be named (OI-128). In this version no action names it: the
/// endpoints it will open (key validation, webhook-key validation, introspection) move onto it in a
/// change of their own, each one reviewed. Until then the scheme is registered and reachable from
/// nowhere, and a stray <c>[Authorize(AuthenticationSchemes = MachineScheme)]</c> on an existing
/// action would be exactly the unreviewed widening this guard exists to catch.
/// </summary>
public class MachineSchemePlacementGuardTests
{
    [Fact]
    public void TheSchemeLiteral_AppearsOnlyInTheBuilder()
    {
        FilesContaining("\"MachineClient\"").Should().Equal("AccessTokenValidation.cs");
    }

    [Fact]
    public void TheSchemeConstant_IsUsedOnlyByTheBuilderAndTheRegistration()
    {
        FilesContaining("MachineScheme").Should().BeEquivalentTo("AccessTokenValidation.cs", "BearerSchemeRegistration.cs");
    }

    private static List<string> FilesContaining(string text)
    {
        var root = Path.Combine(SolutionDirectory(), "Auth_API");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        files.Should().HaveCountGreaterThan(50, "the scan must see the whole API");

        return files
            .Where(file => File.ReadAllText(file).Contains(text, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
    }

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
