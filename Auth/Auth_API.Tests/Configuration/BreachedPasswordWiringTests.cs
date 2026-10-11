using System.Text.Json;
using System.Text.RegularExpressions;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// S09 (T2, T4): the breached-password check is wired so that its settings are read per
/// request. Until S09 the checker was chosen once at boot from <c>breachSettings.Enabled</c>,
/// HIBP or a no-op returning 0, so switching the check on in the console showed "on" while every
/// password still went through unchecked until the next recycle. Nothing in a unit test resolves
/// Program.cs, so this reads its source.
/// </summary>
public class BreachedPasswordWiringTests
{
    [Fact]
    public void Program_RegistersHibpAlways_AndReadsNoBreachSettingAtBoot()
    {
        var program = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Program.cs"));

        program.Should().NotContain("NullBreachedPasswordChecker",
            "a no-op checker chosen at boot is what made Enabled restart-bound");
        program.Should().NotContain("breachSettings.Enabled",
            "whether the check runs is the evaluator's per-request decision");
        // The same boot-time read under the other spellings this file could use: binding the
        // breach section, naming its key, or reaching it through the PasswordSettings that
        // Program.cs already binds at boot for the hasher (`passwordSettings.BreachedPasswordCheck`).
        // `BreachedPasswordCheckSettings.MaxTimeoutMs` is a constant and does not match.
        program.Should().NotContain("Get<BreachedPasswordCheckSettings>");
        program.Should().NotContain("\"Password:BreachedPasswordCheck");
        program.Should().NotContain("BreachedPasswordCheckSettings.SectionName");
        Regex.IsMatch(program, @"\.BreachedPasswordCheck\b").Should().BeFalse(
            "no member access to the breach settings at boot, under any variable name");
        Regex.Matches(program, @"AddHttpClient<IBreachedPasswordChecker,\s*HibpBreachedPasswordChecker>")
            .Should().ContainSingle("the HIBP client is registered unconditionally, once");
        // A top-level statement, not one inside a block (an `if`) that a boot-time value decides.
        Regex.IsMatch(
                program,
                @"^builder\.Services\.AddHttpClient<IBreachedPasswordChecker, HibpBreachedPasswordChecker>\(",
                RegexOptions.Multiline)
            .Should().BeTrue("the registration starts at column 0, outside any block");
        Regex.IsMatch(
                program,
                @"AddHttpClient<IBreachedPasswordChecker,\s*HibpBreachedPasswordChecker>\(client =>\s*\{[^}]*\}\)\.RemoveAllLoggers\(\);")
            .Should().BeTrue(
                "the request URI holds a SHA-1 prefix of the new password, which the factory's loggers would write with the request's scope");
        program.Should().Contain("BreachedPasswordCheckSettings.MaxTimeoutMs",
            "the client's own timeout is the ceiling, not a boot-time copy of TimeoutMs");
    }

    [Fact]
    public void ShippedAppsettings_EnablesTheCheck()
    {
        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        settings.RootElement
            .GetProperty("Password")
            .GetProperty("BreachedPasswordCheck")
            .GetProperty("Enabled")
            .ValueKind.Should().Be(JsonValueKind.True);
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
}
