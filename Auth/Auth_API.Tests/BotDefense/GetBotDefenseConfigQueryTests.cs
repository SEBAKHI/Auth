using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Features.Platform.GetBotDefenseConfig;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS9 (BD8): the pages get a sitekey only where there is something to check. Off, no
/// sitekey, or a testing key in Production: null, and the pages load nothing from Cloudflare.
/// </summary>
/// <remarks>Deliberate break, verified red: the sitekey served under Off.</remarks>
public class GetBotDefenseConfigQueryTests
{
    private static async Task<BotDefenseConfigDto> Ask(BotDefenseSettings settings, bool production = false)
    {
        var handler = new GetBotDefenseConfigQueryHandler(TestHelpers.CreateOptions(settings), new FakeEnvironment(production));
        var result = await handler.Handle(new GetBotDefenseConfigQuery(), CancellationToken.None);
        result.IsError.Should().BeFalse();
        return result.Value;
    }

    [Fact]
    public async Task Off_ServesNoSitekey()
    {
        var config = await Ask(BotDefenseTestDoubles.Settings(BotDefenseMode.Off));

        config.Mode.Should().Be(BotDefenseMode.Off);
        config.SiteKey.Should().BeNull("a deployment left on Off loads nothing from Cloudflare");
    }

    [Fact]
    public async Task NoSitekey_ServesNull()
    {
        (await Ask(BotDefenseTestDoubles.Settings(siteKey: "  "))).SiteKey.Should().BeNull();
    }

    [Theory]
    [InlineData("1x00000000000000000000AA", BotDefenseTestDoubles.Secret)]
    [InlineData(BotDefenseTestDoubles.SiteKey, "1x0000000000000000000000000000000AA")]
    public async Task ATestingKeyInProduction_ServesNull(string siteKey, string secret)
    {
        (await Ask(BotDefenseTestDoubles.Settings(siteKey: siteKey, secret: secret), production: true))
            .SiteKey.Should().BeNull();
    }

    [Fact]
    public async Task ATestingSitekeyOutsideProduction_IsServed()
    {
        (await Ask(BotDefenseTestDoubles.Settings(siteKey: TurnstileTestingKeys.Sitekeys[0])))
            .SiteKey.Should().Be(TurnstileTestingKeys.Sitekeys[0]);
    }

    [Theory]
    [InlineData(BotDefenseMode.Monitor)]
    [InlineData(BotDefenseMode.Enforce)]
    public async Task MonitorAndEnforce_ServeTheSitekeyAndTheMode(BotDefenseMode mode)
    {
        var config = await Ask(BotDefenseTestDoubles.Settings(mode), production: true);

        config.Mode.Should().Be(mode);
        config.SiteKey.Should().Be(BotDefenseTestDoubles.SiteKey);
    }
    [Theory]
    [InlineData(BotDefenseMode.Monitor)]
    [InlineData(BotDefenseMode.Enforce)]
    public async Task NothingToCheck_ReadsOff_WhateverTheMode(BotDefenseMode mode)
    {
        // REVIEW 167 S7: the public answer never says "enforced, nothing checked".
        var noSitekey = await Ask(BotDefenseTestDoubles.Settings(mode, siteKey: ""));
        var testingKey = await Ask(BotDefenseTestDoubles.Settings(mode, siteKey: "1x00000000000000000000AA"), production: true);

        noSitekey.Should().Be(new BotDefenseConfigDto(BotDefenseMode.Off, null));
        testingKey.Should().Be(new BotDefenseConfigDto(BotDefenseMode.Off, null));
    }

}
