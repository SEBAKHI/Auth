using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Domain;

/// <summary>
/// X12, LK8.1: the account-lock event codes are an operator contract: alert rules,
/// dashboards and the day-16 review of X12 search for them. Each value is pinned
/// here, and each follows the signal-code format (LG5): dotted lower-case words,
/// at most 64 characters, unique.
/// </summary>
/// <remarks>Deliberate break: rename one code; the pin turns red.</remarks>
public class AccountLockSignalCodesTests
{
    private static readonly Regex Format = new(@"^[A-Za-z][A-Za-z0-9-]*(\.[A-Za-z0-9][A-Za-z0-9-]*)+$");

    [Fact]
    public void Codes_ArePinned()
    {
        AccountLockSignalCodes.Locked.Should().Be("account.locked");
        AccountLockSignalCodes.HardLocked.Should().Be("account.hard-locked");
        AccountLockSignalCodes.TwoFactorHardLocked.Should().Be("two-factor.hard-locked");
        AccountLockSignalCodes.Unlocked.Should().Be("account.unlocked");
        AccountLockSignalCodes.FamiliarAdmitted.Should().Be("account.familiar-admitted");
    }

    [Fact]
    public void All_ListsEveryConstant_EachWellFormedAndUnique()
    {
        var constants = typeof(AccountLockSignalCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().HaveCount(5);
        AccountLockSignalCodes.All.Should().BeEquivalentTo(constants);
        constants.Should().OnlyHaveUniqueItems();
        constants.Should().AllSatisfy(code =>
        {
            code.Length.Should().BeLessThanOrEqualTo(64);
            Format.IsMatch(code).Should().BeTrue($"{code} must follow the signal-code format");
        });
    }
}
