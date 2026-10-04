using System.Text.RegularExpressions;

namespace Auth_API.Tests.Authentication.OidcUserInfo;

/// <summary>
/// Keeps the application-token scheme isolated to userinfo (X11, P7-R2), on the source text of
/// Auth_API, because the ways to break it are each one innocent-looking line:
/// <list type="bullet">
/// <item>naming the scheme on another action or controller;</item>
/// <item>a default, fallback or policy scheme that forwards to it, which would put it behind every
/// bare <c>[Authorize]</c> and <c>[RequirePermission]</c>;</item>
/// <item>a second literal of the validation parameters, or a bearer registration that maps claim
/// types or reads the token from somewhere other than the Authorization header.</item>
/// </list>
/// </summary>
public class UserInfoSchemeIsolationGuardTests
{
    private const string BuilderFile = "AccessTokenValidation.cs";
    private const string RegistrationFile = "BearerSchemeRegistration.cs";
    private const string ControllerFile = "AuthController.cs";

    // --- (a) the scheme is named only where it must be ---

    [Fact]
    public void TheSchemeLiteral_AppearsOnlyInTheBuilder()
    {
        FilesContaining("\"OidcUserInfo\"").Should().Equal(BuilderFile);
    }

    [Fact]
    public void TheSchemeConstant_IsUsedOnlyByTheBuilderTheRegistrationAndTheUserInfoAction()
    {
        FilesContaining("UserInfoScheme").Should().BeEquivalentTo(BuilderFile, RegistrationFile, ControllerFile);

        var controller = Source(ControllerFile);
        Regex.Matches(controller, @"\bUserInfoScheme\b").Should().ContainSingle(
            "one [Authorize] on the userinfo action and nowhere else in the controller");

        var index = controller.IndexOf("AccessTokenValidation.UserInfoScheme", StringComparison.Ordinal);
        var following = controller[index..Math.Min(controller.Length, index + 600)];
        following.Should().Contain("public async Task<IActionResult> GetOidcUserInfo(",
            "the attribute must sit on the userinfo action");
        controller[Math.Max(0, index - 300)..index].Should().Contain("[HttpGet(\"userinfo\")]");
    }

    // --- (b) nothing makes it a default, a fallback or a forwarding target ---

    public static TheoryData<string> ForbiddenApis => new()
    {
        "SetDefaultPolicy(",
        "SetFallbackPolicy(",
        "DefaultPolicy =",
        "FallbackPolicy =",
        "AddPolicyScheme(",
        "ForwardDefaultSelector",
        "ForwardDefault =",
        "ForwardAuthenticate =",
        "ForwardChallenge =",
        "DefaultScheme =",
    };

    [Fact]
    public void ForbiddenApis_IsNotEmpty() => ForbiddenApis.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(ForbiddenApis))]
    public void NoDefaultFallbackOrForwardingScheme(string api)
    {
        FilesContaining(api).Should().BeEmpty(
            "{0} would route requests that name no scheme to one; the default scheme stays the "
            + "platform scheme, set only by DefaultAuthenticateScheme and DefaultChallengeScheme", api);
    }

    [Fact]
    public void PermissionPolicies_StaySchemeLess()
    {
        // [RequirePermission] builds its policy here; a scheme added to it would admit the
        // application token to every permission-guarded endpoint.
        Source("PermissionPolicyProvider.cs").Should().NotContain("AuthenticationScheme");
    }

    // --- (c) one builder, one registration, header only, original claim names ---

    [Fact]
    public void EveryBearerRegistration_IsInTheRegistration_AndGoesThroughItsOneConfigure()
    {
        FilesContaining("AddJwtBearer(").Should().Equal(RegistrationFile);

        var registration = Source(RegistrationFile);
        var registrations = Regex.Matches(registration, @"\.AddJwtBearer\(").Count;
        registrations.Should().Be(2, "the platform scheme and the userinfo scheme");
        Regex.Matches(registration, @"options => Configure\(options, AccessTokenValidation\.").Count
            .Should().Be(registrations, "each scheme is configured by the one Configure");

        registration.Should().Contain("options.MapInboundClaims = false;");
        Regex.Matches(registration, @"options\.MapInboundClaims\s*=").Should().ContainSingle();
    }

    [Fact]
    public void NoBearerRegistration_ReadsTheTokenFromAnywhereButTheHeader()
    {
        // Program.cs's comment says why: a token from a query string or a body skips the blacklist.
        FilesContaining("OnMessageReceived =").Should().BeEmpty();
        FilesContaining("OnMessageReceived=").Should().BeEmpty();
    }

    [Fact]
    public void TheValidationParameters_AreBuiltOnlyByTheBuilder()
    {
        // The builder makes its one base set with a target-typed new(); any other construction,
        // in any form, is a second copy.
        FilesContaining("new TokenValidationParameters").Should().BeEmpty();
        FilesContaining("TokenValidationParameters = new").Should().BeEmpty();
        Regex.Matches(Source(BuilderFile), @"TokenValidationParameters \w+\([^)]*\) => new\(\)")
            .Should().ContainSingle("one base set, from which both profiles start");

        var program = Source("Program.cs");
        program.Should().Contain("builder.Services.AddAuthSystemBearerSchemes(jwtSettings, jwtTokenService.GetSecurityKey());");
        program.Should().NotContain("AddAuthentication(", "the registration owns it");
    }

    #region Helpers

    /// <summary>The non-test C# files of Auth_API, by file name, that contain <paramref name="text"/>.</summary>
    private static List<string> FilesContaining(string text) =>
        SourceFiles()
            .Where(file => File.ReadAllText(file).Replace("\r\n", "\n").Contains(text, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

    private static string Source(string fileName)
    {
        var matches = SourceFiles().Where(file => Path.GetFileName(file) == fileName).ToList();
        matches.Should().ContainSingle("{0} must exist exactly once in Auth_API", fileName);
        return File.ReadAllText(matches[0]).Replace("\r\n", "\n");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = Path.Combine(SolutionDirectory(), "Auth_API");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        files.Should().HaveCountGreaterThan(50, "the scan must see the whole API");
        return files;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }

    #endregion
}
