using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Domain;

/// <summary>
/// X04, ARR-0501: the revocation list's load codes are an operator contract: a deployment's
/// check after a restart, and a log filter that keeps lines by code at any level, search for
/// them. Each value is pinned here, and each follows the signal-code format (LG5): dotted
/// lower-case words, at most 64 characters, unique.
/// </summary>
/// <remarks>Deliberate break: rename one code; the pin turns red.</remarks>
public class RevocationSignalCodesTests
{
    private static readonly Regex Format = new(@"^[A-Za-z][A-Za-z0-9-]*(\.[A-Za-z0-9][A-Za-z0-9-]*)+$");

    [Fact]
    public void Codes_ArePinned()
    {
        RevocationSignalCodes.Rehydrated.Should().Be("revocation.rehydrated");
        RevocationSignalCodes.RehydrationFailed.Should().Be("revocation.rehydration-failed");
    }

    [Fact]
    public void All_ListsEveryConstant_EachWellFormedAndUnique()
    {
        var constants = typeof(RevocationSignalCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().HaveCount(2);
        RevocationSignalCodes.All.Should().BeEquivalentTo(constants);
        constants.Should().OnlyHaveUniqueItems();
        constants.Should().AllSatisfy(code =>
        {
            code.Length.Should().BeLessThanOrEqualTo(64);
            Format.IsMatch(code).Should().BeTrue($"{code} must follow the signal-code format");
        });
    }
}
