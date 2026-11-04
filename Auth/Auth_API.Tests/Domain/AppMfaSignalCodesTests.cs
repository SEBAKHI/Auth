using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Domain;

/// <summary>
/// S23: the event codes of an application's two-step requirement are an operator
/// contract: the readiness reading before Enforce and the condition for removing the
/// switch count them. Each value is pinned here, and each follows the signal-code format
/// (LG5): dotted lower-case words, at most 64 characters, unique.
/// </summary>
/// <remarks>Deliberate break: rename one code; the pin turns red.</remarks>
public class AppMfaSignalCodesTests
{
    private static readonly Regex Format = new(@"^[A-Za-z][A-Za-z0-9-]*(\.[A-Za-z0-9][A-Za-z0-9-]*)+$");

    [Fact]
    public void Codes_ArePinned()
    {
        AppMfaSignalCodes.WouldRequire.Should().Be("app-mfa.would-require");
        AppMfaSignalCodes.Refused.Should().Be("app-mfa.refused");
        AppMfaSignalCodes.GuardDenied.Should().Be("app-mfa.guard-denied");
    }

    [Fact]
    public void All_ListsEveryConstant_EachWellFormedAndUnique()
    {
        var constants = typeof(AppMfaSignalCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().HaveCount(3);
        AppMfaSignalCodes.All.Should().BeEquivalentTo(constants);
        constants.Should().OnlyHaveUniqueItems();
        constants.Should().AllSatisfy(code =>
        {
            code.Length.Should().BeLessThanOrEqualTo(64);
            Format.IsMatch(code).Should().BeTrue($"{code} must follow the signal-code format");
        });
    }
}
