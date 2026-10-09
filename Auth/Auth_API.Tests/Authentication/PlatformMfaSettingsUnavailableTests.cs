using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Configuration;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08 PR B, deviation row 89 (1)(b), design D-B7: the enforcement switch fails
/// closed. Until the database settings have loaded once, the API does not know
/// what an administrator saved, so it enforces — but a failed periodic refresh,
/// which keeps the values last read, changes nothing, and the escape hatch that
/// switches the database layer off keeps the file's value. The warning is written
/// once per process (F5), and the real provider's load state is tested both ways
/// through its row-query seam (F4): a success is what turns enforcement back off.
/// </summary>
public class PlatformMfaSettingsUnavailableTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly TokenClaims Admin = new(["super-admin"], ["*"], []);

    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ILogger<PlatformMfaPolicy>> _logger = new();
    // One per process in the API (a singleton); one per test here.
    private readonly EnforcedSettingsWarning _warning = new();

    private PlatformMfaPolicy Policy(bool fileSaysEnforce, ISystemSettingsReloader reloader) =>
        new(
            _store.Object,
            Mock.Of<ITokenClaimsResolver>(),
            TestHelpers.CreateOptions(new TwoFactorSettings { EnforceForPlatformAdmins = fileSaysEnforce }),
            reloader,
            _warning,
            _logger.Object);

    private static ISystemSettingsReloader Reloader(bool hasLoaded, bool lastLoadFailed)
    {
        var reloader = new Mock<ISystemSettingsReloader>();
        reloader.SetupGet(r => r.HasLoadedSinceStart).Returns(hasLoaded);
        reloader.SetupGet(r => r.LastLoadFailed).Returns(lastLoadFailed);
        return reloader.Object;
    }

    [Fact]
    public void NeverLoaded_Enforces_WithItsOwnWarning_OncePerProcess()
    {
        var reloader = Reloader(hasLoaded: false, lastLoadFailed: true);
        var firstRequest = Policy(fileSaysEnforce: false, reloader);
        var secondRequest = Policy(fileSaysEnforce: false, reloader);

        firstRequest.IsEnforcing.Should().BeTrue("what an administrator saved has never been read");
        firstRequest.IsEnforcedFor(Admin).Should().BeTrue();
        secondRequest.IsEnforcing.Should().BeTrue();
        // Two requests (the policy is scoped), three reads: one warning, however
        // long the cause lasts.
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.StartsWith("PlatformMfa.EnforcedSettingsUnavailable", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task NeverLoaded_WithholdsAtTheMint()
    {
        _store.Setup(s => s.HasEnabledFactorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var policy = Policy(fileSaysEnforce: false, Reloader(hasLoaded: false, lastLoadFailed: true));

        var decision = await policy.EvaluateAsync(UserId, null, Admin, AuthenticationMethods.Password, CancellationToken.None);

        decision.Withheld.Should().BeTrue();
    }

    [Fact]
    public void LoadedThenARefreshFailed_KeepsTheLastValueRead()
    {
        // The provider keeps the last good values after a failed refresh, so the
        // options still say what was saved: LastLoadFailed alone must not enforce.
        var policy = Policy(fileSaysEnforce: false, Reloader(hasLoaded: true, lastLoadFailed: true));

        policy.IsEnforcing.Should().BeFalse();
        policy.IsEnforcedFor(Admin).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loaded_ReadsTheSwitch(bool saved)
    {
        Policy(fileSaysEnforce: saved, Reloader(hasLoaded: true, lastLoadFailed: false))
            .IsEnforcing.Should().Be(saved);
    }

    [Fact]
    public void EscapeHatch_KeepsTheFileValue()
    {
        // AUTH_DISABLE_DB_SETTINGS: the files are the whole configuration.
        var reloader = new NullSystemSettingsReloader();

        reloader.HasLoadedSinceStart.Should().BeTrue();
        Policy(fileSaysEnforce: false, reloader).IsEnforcing.Should().BeFalse();
    }

    [Fact]
    public void Provider_AFailedLoad_HasNotLoaded()
    {
        // A connection string the client cannot even parse fails at once, the way an
        // unreachable database fails after its timeout.
        var provider = new DbSettingsConfigurationProvider(
            "this is not a connection string",
            new Dictionary<string, int>());

        provider.Load();

        provider.LastLoadFailed.Should().BeTrue();
        provider.HasLoadedSinceStart.Should().BeFalse();

        provider.Reload();

        provider.HasLoadedSinceStart.Should().BeFalse("only a successful load counts");
    }

    // ── F4: the real provider's load state, through its row-query seam ──

    private static DbSettingsConfigurationProvider Provider(Func<List<(string SectionKey, string OverridesJson)>> rows) =>
        new(rows, new Dictionary<string, int>());

    [Fact]
    public void Provider_ASuccessfulLoad_HasLoaded()
    {
        var provider = Provider(() => []);

        provider.Load();

        provider.LastLoadFailed.Should().BeFalse();
        provider.HasLoadedSinceStart.Should().BeTrue("a success is what turns the boot-time enforcement off");
    }

    [Fact]
    public void Provider_AFailedLoadThenASuccessfulRefresh_HasLoaded()
    {
        var calls = 0;
        var provider = Provider(() => ++calls == 1 ? throw new InvalidOperationException("the database is not there yet") : []);

        provider.Load();
        provider.HasLoadedSinceStart.Should().BeFalse();

        provider.Reload();

        provider.LastLoadFailed.Should().BeFalse();
        provider.HasLoadedSinceStart.Should().BeTrue("the periodic refresh ends the window");
    }

    [Fact]
    public void Provider_ASuccessfulLoadThenAFailedRefresh_StaysLoaded()
    {
        var calls = 0;
        var provider = Provider(() => ++calls == 1 ? [] : throw new InvalidOperationException("the database went away"));

        provider.Load();
        provider.Reload();

        provider.LastLoadFailed.Should().BeTrue();
        provider.HasLoadedSinceStart.Should().BeTrue("a failed refresh keeps the values last read");
    }

    [Fact]
    public void NoPlatformPermission_NeverEnforced_EvenBeforeALoad()
    {
        Policy(fileSaysEnforce: false, Reloader(hasLoaded: false, lastLoadFailed: true))
            .IsEnforcedFor(new TokenClaims(["user"], [], [])).Should().BeFalse(
                "two-step verification stays optional for accounts without platform permissions");
    }
}
