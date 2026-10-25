using System.Text.RegularExpressions;
using Auth.Domain.ValueObjects;
using Auth.Sdk.Authorization;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// OI-225 (F4 of REVIEW 104): one permission rule. The API's matchers all call
/// <see cref="PermissionCode.Matches"/>; the SDK, which cannot reference the domain,
/// has one internal matcher, held to the same case table here. A held code outside the
/// format matches only itself, so a held <c>*:*</c> never satisfies <c>*</c>.
/// </summary>
/// <remarks>
/// Deliberate breaks: restore one of the old copies (the guard turns red); drop the
/// well-formedness check from the SDK matcher (the parity table turns red).
/// </remarks>
public class PermissionMatcherParityTests
{
    /// <summary>Held, required, expected.</summary>
    public static TheoryData<string, string, bool> Cases() => new()
    {
        { "*", "users:read", true },
        { "*", "*", true },
        { "crm:*", "crm", true },
        { "crm:*", "crm:leads:read", true },
        { "crm:*", "billing:invoices:read", false },
        { "crm:*", "crmx:read", false },
        { "crm:leads:*", "crm:leads:read", true },
        { "crm:leads:*", "crm:deals:read", false },
        { "users:read", "users:read", true },
        { "users:read", "users:update", false },
        { "Users:Read", "users:read", true },       // exact, case-insensitive
        { "CRM:*", "crm:leads:read", true },        // a stored upper-case wildcard keeps matching
        { "secrets.manage", "secrets.manage", true },
        { "secrets:*", "secrets.manage", false },
        { "*:*", "*", false },                       // malformed: only itself
        { "*:*", "users:read", false },
        { "*:*", "*:*", true },
        { "a:*:b", "a:x:b", false },
        { "users*", "users", false },
        { "*\n", "users:read", false },              // a trailing newline is outside the format
        { "crm:*\n", "crm:leads:read", false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheApiRule_AnswersTheTable(string held, string required, bool expected)
    {
        PermissionCode.From(held).Matches(required).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheSdkMatcher_AnswersTheSameTable(string held, string required, bool expected)
    {
        PermissionMatcher.Matches(held, required).Should().Be(expected);
        PermissionMatcher.MatchesAny(["unrelated:read", held], required).Should().Be(expected);
    }

    [Fact]
    public void TheSdkMatcher_HasTheApisPattern()
    {
        PermissionMatcher.Pattern.Should().Be(PermissionCode.Pattern);
    }

    /// <summary>
    /// No other prefix-wildcard matcher in production code: a new copy would drift from
    /// the rule without failing anything. The one other <c>":*"</c> test classifies an
    /// account as holding a wildcard, to warn on an external sign-in link; it grants
    /// nothing, and a held malformed code counted there only adds a warning.
    /// </summary>
    [Fact]
    public void NoOtherWildcardMatcher_RemainsInProductionCode()
    {
        var wildcardTest = new Regex(@"EndsWith\(\s*""\:\*""");
        var files = ApiSourceScan.ProductionSources()
            .Where(source => wildcardTest.IsMatch(source.Source))
            .Select(source => Path.GetFileName(source.File))
            .ToList();

        files.Should().NotBeEmpty("the scan must find the rule itself, or it proves nothing");
        files.Should().BeEquivalentTo(
            ["PermissionCode.cs", "PermissionMatcher.cs", "ExternalLoginCommandHandler.cs"],
            "every other matcher calls PermissionCode.Matches (API) or PermissionMatcher (SDK)");
    }
}
