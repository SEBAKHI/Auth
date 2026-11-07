using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Features.Authentication.RemovePassword;
using Auth.Application.Features.Passkeys.Common;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-150-4 «off means off» (T-B6): who weighs a factor by what can be USED, and who
/// keeps the inventory. A rule that demands a passkey or leans on one reads
/// <see cref="UsableSecondFactors"/>, which masks the passkey while passkeys do not run; only the
/// readers listed here read the inventory, each with its reason. A new reader fails here until it
/// says which it is.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the setup of the app reading the inventory (the store
/// call appears in its file); the switch-off of the app no longer reading the inventory (N13: its
/// passkey check gone, so a live passkey no longer keeps the app while email is off).
/// </remarks>
public class UsableSecondFactorsReaderGuardTests
{
    /// <summary>The files that read the inventory, and why.</summary>
    private static readonly Dictionary<string, string> InventoryReaders = new()
    {
        ["UsableSecondFactors.cs"] = "the one place the mask is applied",
        ["DisableTwoFactorCommandHandler.cs"] =
            "N13, D-150-5 (a): with email off a live passkey keeps the app, whatever Passkeys:Enabled says",
        ["ResetUserTwoFactorCommandHandler.cs"] = "M8(4): an administrator's reset sees what the account holds",
        ["PasskeyRegistrationGate.cs"] = "the passkey routes themselves, which answer 404 while passkeys do not run",
        ["RemovePasskeyCommandHandler.cs"] = "the passkey routes themselves",
        ["GetUserByIdQueryHandler.cs"] = "the console's user page shows what the account holds",
    };

    /// <summary>The rules that weigh a factor, each through the usable reader.</summary>
    private static readonly string[] UsableReaders =
    [
        "PlatformMfaPolicy.cs",
        "ApplicationMfaPolicy.cs",
        "SetupTwoFactorCommandHandler.cs",
        "EnableTwoFactorCommandHandler.cs",
        "SendTwoFactorEmailCodeCommandHandler.cs",
        "PlatformGrantFactorGuard.cs",
        "TwoFactorResetAdministrators.cs",
        "GetApplicationMfaReadinessQueryHandler.cs",
        "PasskeyOnlyAdministratorRule.cs",
    ];

    [Fact]
    public void EveryReadOfTheInventory_IsTheMaskOrSaysWhyItKeepsTheInventory()
    {
        var readers = ApiSourceScan.ProductionSources()
            .Where(file => Regex.IsMatch(file.Source, @"\.\s*(GetQualifyingFactorsAsync|HasEnabledFactorAsync)\s*\("))
            .Select(file => Path.GetFileName(file.File))
            .ToList();

        readers.Should().BeEquivalentTo(InventoryReaders.Keys,
            "a rule that decides what a factor is worth reads UsableSecondFactors (D-150-4); a reader of the inventory says why here");
    }

    [Fact]
    public void TheRulesThatWeighAFactor_HoldTheUsableReader()
    {
        var holders = ApiSourceScan.ProductionSources()
            .Where(file => Regex.IsMatch(file.Source, @"\bUsableSecondFactors\s+\w+\s*[,)]"))
            .Select(file => Path.GetFileName(file.File))
            .ToList();

        holders.Should().BeEquivalentTo(UsableReaders);
    }

    [Fact]
    public void RemovingThePassword_CannotCountAPasskey()
    {
        // D-S20-5 (b), REVIEW 151 F8: its seventh condition (2b) reads the authenticator app alone.
        // A passkey could only enter it through the passkey store or the FA2 reads, and it holds
        // neither: an administrator with no password and a passkey alone would be opened by the
        // mailbox while passkeys are off.
        typeof(RemovePasswordCommandHandler).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should().NotContain(typeof(Auth.Domain.Interfaces.Repositories.IUserPasskeyRepository));

        var source = ApiSourceScan.ProductionSources()
            .Single(file => file.File.EndsWith("RemovePasswordCommandHandler.cs", StringComparison.Ordinal))
            .Source;
        Regex.IsMatch(source, @"\.\s*(GetQualifyingFactorsAsync|HasEnabledFactorAsync)\s*\(").Should().BeFalse(
            "the app is read from the factor row's snapshot, never from the inventory that counts a passkey");
    }

    [Theory]
    [InlineData(typeof(DisableTwoFactorCommandHandler))]
    [InlineData(typeof(RemovePasswordCommandHandler))]
    public void TheHandlersThatKeepTheInventory_CannotSeeThePasskeySwitch(Type handler)
    {
        // DisableTwoFactor 2b keeps the app while a passkey is live (N13); RemovePassword 2b asks
        // for the app alone (D-S20-5 (b)). Neither answer moves when passkeys are switched off.
        var parameters = handler.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        parameters.Should().NotBeEmpty();
        parameters.Should().NotContain(typeof(UsableSecondFactors))
            .And.NotContain(typeof(PasskeyFeature))
            .And.NotContain(typeof(IOptionsMonitor<PasskeySettings>))
            .And.NotContain(typeof(IOptionsSnapshot<PasskeySettings>))
            .And.NotContain(typeof(IOptions<PasskeySettings>));
    }
}
