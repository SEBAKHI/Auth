using System.Text.RegularExpressions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S12: every access token this server signed is validated with parameters from the one builder
/// (<c>AccessTokenValidation</c>), whose keys come from the signer's key set, token by token. Read
/// off the source text of the three server projects, because the way to break it is one
/// innocent-looking line: a hand-written parameter set over a fixed key, which keeps trusting a key
/// the set has dropped and goes on refusing the one it added. <c>JwtTokenService</c> held such a
/// set until S12.
/// </summary>
/// <remarks>
/// <c>UserInfoSchemeIsolationGuardTests</c> keeps the API project itself free of any other
/// construction; this guard covers the layers it calls into, where the only parameter sets left
/// validate keys this server does not own.
/// </remarks>
public class TokenValidationSiteGuardTests
{
    /// <summary>
    /// The only sites that may build parameters by hand, each over keys that are not this server's:
    /// Apple's published keys, and a client application's own assertion key.
    /// </summary>
    private static readonly string[] ForeignKeySites = ["AppleAuthProvider.cs", "ClientAssertionVerifier.cs"];

    private const string BuilderFile = "AccessTokenValidation.cs";

    [Fact]
    public void ParameterSets_AreBuiltOnlyByTheBuilder_OrOverKeysThisServerDoesNotOwn()
    {
        // The builder makes its one base set with a target-typed new(), so it never appears here.
        FilesMatching(@"new\s+TokenValidationParameters\b").Should().BeEquivalentTo(ForeignKeySites);
        FilesMatching(@"TokenValidationParameters\s*=\s*new\b").Should().BeEmpty();
    }

    [Fact]
    public void NoFixedKey_IsGivenToAValidator_ButAForeignOne()
    {
        // IssuerSigningKey (one fixed key) or IssuerSigningKeys (a fixed list): either captures this
        // server's key at startup, which the key set exists to prevent.
        FilesMatching(@"\bIssuerSigningKeys?\s*=").Should().BeEquivalentTo(ForeignKeySites);
    }

    [Fact]
    public void TheBuilder_ResolvesItsKeysFromTheKeySet()
    {
        var builder = Source(BuilderFile);

        Regex.Matches(builder, @"\bIssuerSigningKeyResolver\s*=").Should().ContainSingle(
            "the one base set resolves its keys, and the profiles inherit it");
        builder.Should().Contain("signingKeys.VerificationKeys(now)",
            "the keys are those the set accepts at the moment of validation");
    }

    [Fact]
    public void TheSigner_ValidatesNothing_AndSignsWithTheCurrentKey()
    {
        var signer = Source("JwtTokenService.cs");

        signer.Should().NotContain("TokenValidationParameters");
        signer.Should().NotContain("ValidateToken(");
        Regex.Matches(signer, @"SigningCredentials = SigningKeys\.Current\b").Count.Should().Be(2,
            "the user token and the machine token, both read at sign time");
        Regex.Matches(signer, @"SigningCredentials = (?!SigningKeys\.Current\b)").Should().BeEmpty(
            "no token is signed with credentials captured at startup");
    }

    #region Helpers

    private static readonly string[] ServerProjects = ["Auth_API", "Auth.Infrastructure", "Auth.Application"];

    /// <summary>The non-test C# files of the three server projects, by file name, matching <paramref name="pattern"/>.</summary>
    private static List<string> FilesMatching(string pattern) =>
        SourceFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), pattern))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

    private static string Source(string fileName)
    {
        var matches = SourceFiles().Where(file => Path.GetFileName(file) == fileName).ToList();
        matches.Should().ContainSingle("{0} must exist exactly once in the server projects", fileName);
        return File.ReadAllText(matches[0]).Replace("\r\n", "\n");
    }

    private static List<string> SourceFiles()
    {
        var root = SolutionDirectory();
        var files = ServerProjects
            .Select(project => Path.Combine(root, project))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        files.Should().HaveCountGreaterThan(300, "the scan must see all three projects");
        return files;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the solution tree");
        return directory!.FullName;
    }

    #endregion
}
