using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Security;

/// <summary>
/// ARR-0694: email sending switched off while accounts depend on the emailed second
/// factor leaves each of them only its recovery codes, so the posture says so, at
/// Warning, for as long as it lasts.
/// </summary>
public class EmailCodeFactorPostureContributorTests
{
    private static EmailCodeFactorPostureContributor Contributor(bool emailEnabled, int accounts, int passwordless = 0)
    {
        var store = new Mock<ITwoFactorStateStore>();
        store.Setup(s => s.CountEmailCodeFactorsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.CountEmailCodeOnlyAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(passwordless);
        var services = new ServiceCollection()
            .AddSingleton<IOptionsSnapshot<EmailSettings>>(TestHelpers.CreateOptions(new EmailSettings { Enabled = emailEnabled }))
            .AddSingleton(store.Object)
            .AddSingleton(users.Object)
            .BuildServiceProvider();

        return new EmailCodeFactorPostureContributor(services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task EmailOff_WithAccountsOnTheEmailedCode_IsWeak_AtWarning()
    {
        var posture = await Contributor(emailEnabled: false, accounts: 3).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Lines.Should().ContainSingle(line => line.Level == LogLevel.Warning && line.Arguments.Contains(3));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    [InlineData(true, 0)]
    public async Task OtherwiseItIsOk_AndStatesTheCount(bool emailEnabled, int accounts)
    {
        var posture = await Contributor(emailEnabled, accounts).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().ContainSingle(line => line.Level == LogLevel.Information && line.Arguments.Contains(accounts));
    }

    [Fact]
    public async Task EmailOff_WithAccountsThatSignInOnlyByCode_IsWeak_AndNamesThem()
    {
        // REVIEW 110 F8: email switched off by configuration (not through the
        // console's confirmation) leaves every passwordless account without a way in.
        var posture = await Contributor(emailEnabled: false, accounts: 0, passwordless: 4).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Lines.Should().ContainSingle(line => line.Level == LogLevel.Warning
            && line.MessageTemplate.Contains("no password") && line.Arguments.Contains(4));
    }

    [Fact]
    public async Task EmailOff_WithBoth_WarnsOfEach()
    {
        var posture = await Contributor(emailEnabled: false, accounts: 2, passwordless: 5).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Lines.Where(line => line.Level == LogLevel.Warning).Should().HaveCount(2);
    }

    [Fact]
    public async Task EmailOn_WithPasswordlessAccounts_IsOk_AndStatesTheCount()
    {
        var posture = await Contributor(emailEnabled: true, accounts: 0, passwordless: 4).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().ContainSingle(line => line.Level == LogLevel.Information && line.Arguments.Contains(4));
    }

    [Fact]
    public async Task AChangeOfCountOrSwitch_IsANewPosture_SoItIsWrittenAgain()
    {
        var off = await Contributor(false, 3).EvaluateAsync(CancellationToken.None);
        var more = await Contributor(false, 4).EvaluateAsync(CancellationToken.None);
        var on = await Contributor(true, 3).EvaluateAsync(CancellationToken.None);
        var passwordless = await Contributor(false, 3, passwordless: 1).EvaluateAsync(CancellationToken.None);

        new[] { off.State, more.State, on.State, passwordless.State }.Should().OnlyHaveUniqueItems();
    }
}
