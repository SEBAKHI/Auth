using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// REVIEW-FIXES 152 F28: <c>Verify(p => p.Publish(It.IsAny&lt;object&gt;(), …), Times.Never)</c> can
/// never fail. The argument's type binds MediatR's non-generic <c>Publish(object, …)</c>, because
/// <c>object</c> does not meet the generic overload's <c>INotification</c> constraint, while every
/// handler calls the generic one; so the check passes after an event was published. Twelve such
/// checks stood, four of them on the "Confirm it's you" window. A check that nothing was published
/// names the event its handler publishes.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: one of the corrected checks put back as it was.
/// </remarks>
public class VacuousPublishVerifyGuardTests
{
    // A Verify whose expression is a Publish of It.IsAny<object>(), whatever the names and spacing.
    private static readonly Regex VacuousVerify = new(
        @"\.Verify\(\s*\w+\s*=>\s*\w+\.Publish\(\s*It\.IsAny<object>\(\)",
        RegexOptions.Singleline);

    /// <summary>Every C# file of the test projects, build output excluded.</summary>
    private static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(ApiSourceScan.SolutionDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => path.Contains("Tests", StringComparison.Ordinal));

    [Fact]
    public void NoTest_ChecksThatNothingWasPublished_ThroughTheNonGenericOverload()
    {
        var root = ApiSourceScan.SolutionDirectory();
        var found = TestSources()
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return VacuousVerify.Matches(source).Select(match =>
                    $"{Path.GetRelativePath(root, path)}:{source[..match.Index].Count(c => c == '\n') + 1}");
            })
            .ToList();

        found.Should().BeEmpty(
            "a Verify of Publish(It.IsAny<object>()) binds the overload no handler calls, so it passes whatever was published: name the event instead");
    }

    [Fact]
    public void TheScan_SeesTheTestProjects_AndTheShape()
    {
        // The positive controls: a scan that reads no test file, or a pattern that matches no
        // vacuous check, proves nothing. The sample is built in two parts so this file holds none.
        TestSources().Should().Contain(path => path.EndsWith("VacuousPublishVerifyGuardTests.cs", StringComparison.Ordinal));

        var vacuous = "_publisher.Verify(p => p.Publish(It.IsAny<" + "object>(), It.IsAny<CancellationToken>()), Times.Never);";
        var spread = "raced.Publisher.Verify(\n    p => p.Publish(It.IsAny<" + "object>(), It.IsAny<CancellationToken>()),\n    Times.Never);";
        var named = "_publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);";

        VacuousVerify.IsMatch(vacuous).Should().BeTrue();
        VacuousVerify.IsMatch(spread).Should().BeTrue("a call spread over lines is the same call");
        VacuousVerify.IsMatch(named).Should().BeFalse("a named event binds the overload the handlers call");
    }
}
