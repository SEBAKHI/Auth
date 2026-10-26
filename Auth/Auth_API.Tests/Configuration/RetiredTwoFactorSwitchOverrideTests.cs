using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.SystemSettings.GetSystemSettings;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Configuration;
using Auth_API.Tests.Administration;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// OI-47 removed the two rollout switches of the two-factor card. A deployment that
/// saved the card while they existed still holds a TwoFactor override row naming
/// them. That row must be inert — neither switch comes back from it — and must heal
/// itself: the card still reads and saves, and the next save drops the old keys.
/// A row that blocked the save would leave the card unsaveable: a new dead end.
/// </summary>
public class RetiredTwoFactorSwitchOverrideTests
{
    private const string StoredRow =
        """{"RejectReusedCodes":false,"RequireEmailCodeForFirstFactor":false,"ReauthenticationMaxAgeMinutes":20}""";

    private static readonly string[] RetiredKeys = ["RejectReusedCodes", "RequireEmailCodeForFirstFactor"];

    private static DbSettingsConfigurationProvider LoadedProvider()
    {
        var provider = new DbSettingsConfigurationProvider(() => [("TwoFactor", StoredRow)], new Dictionary<string, int>());
        provider.Load();
        return provider;
    }

    [Fact]
    public void TheDatabaseProvider_LoadsTheRowsLiveKey_AndNeitherRetiredSwitch()
    {
        var provider = LoadedProvider();

        provider.LastLoadFailed.Should().BeFalse();
        provider.TryGet("TwoFactor:ReauthenticationMaxAgeMinutes", out var live).Should().BeTrue();
        live.Should().Be("20", "the row's live field still applies");
        foreach (var key in RetiredKeys)
        {
            provider.TryGet($"TwoFactor:{key}", out _).Should().BeFalse($"{key} is no longer a setting, so the stored false is never loaded");
        }
    }

    [Fact]
    public void WithTheRowLoaded_TheMailboxProofIsStillRequired_AndNoStoreCallCanRelaxTheStepClaim()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Enabled"] = "true" })
            .Add(new ProviderSource(LoadedProvider()))
            .Build();

        var email = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>()!;
        var twoFactor = configuration.GetSection(TwoFactorSettings.SectionName).Get<TwoFactorSettings>()!;

        twoFactor.ReauthenticationMaxAgeMinutes.Should().Be(20);
        new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(email)).IsRequired
            .Should().BeTrue("with email on a first factor needs the emailed code, whatever the row says");

        // A reused code is refused by the store's step claim. No store method takes a
        // parameter a caller could pass to relax it, so nothing read from a row
        // reaches it; the registry and settings pins are the guard on the setting.
        var parameters = typeof(ITwoFactorStateStore).GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => (Method: method.Name, Parameter: parameter.Name!)))
            .ToList();
        parameters.Should().NotBeEmpty();
        parameters.Where(p => p.Parameter.StartsWith("reject", StringComparison.OrdinalIgnoreCase)
                || p.Parameter.Contains("reuse", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the store holds no switch that lets a reused step through");
    }

    [Fact]
    public async Task TheCardRead_ShowsNoFieldForEitherRetiredSwitch()
    {
        var repository = new Mock<ISystemSettingsRepository>();
        repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SystemSettingsTestSupport.ExistingRow("TwoFactor", StoredRow)]);
        var users = new Mock<IUserRepository>();
        users
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        var configuration = new ConfigurationBuilder().Add(new ProviderSource(LoadedProvider())).Build();

        var result = await new GetSystemSettingsQueryHandler(
                repository.Object, users.Object, configuration, SystemSettingsTestSupport.SnapshotOf(configuration),
                new Mock<ISystemSettingsReloader>().Object)
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var card = result.Value.Sections.Single(s => s.Key == "TwoFactor");
        card.Fields.Select(f => f.Path).Should().BeEquivalentTo(
            ["ReauthenticationMaxAgeMinutes", "EnforceForPlatformAdmins", "EmailCodeLifetimeMinutes"]);
        card.Fields.Single(f => f.Path == "ReauthenticationMaxAgeMinutes").OverrideValue.Should().Be(20L);
    }

    [Fact]
    public async Task SavingTheCardWithWhatItShows_Succeeds_AndTheStoredRowLosesBothRetiredKeys()
    {
        var rowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        var repository = new Mock<ISystemSettingsRepository>();
        repository
            .Setup(r => r.GetAsync("TwoFactor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("TwoFactor", StoredRow, rowVersion));
        repository
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 1, 1, 1, 1, 1, 1, 1], 2));
        var users = new Mock<IUserRepository>();
        users
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        var configuration = new ConfigurationBuilder().Add(new ProviderSource(LoadedProvider())).Build();
        var handler = new UpdateSystemSettingsCommandHandler(
            repository.Object,
            users.Object,
            configuration,
            SystemSettingsTestSupport.SnapshotOf(configuration),
            new Mock<ISystemSettingsReloader>().Object,
            new Mock<IPublisher>().Object,
            new Mock<IIdpSessionIdleLimitChange>().Object,
            new Mock<ILogger<UpdateSystemSettingsCommandHandler>>().Object);

        // The console sends the card's own fields: the read above shows no other.
        var result = await handler.Handle(
            new UpdateSystemSettingsCommand(
                "TwoFactor",
                SystemSettingsTestSupport.Json("""{"ReauthenticationMaxAgeMinutes":20}"""),
                Convert.ToBase64String(rowVersion),
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse("the card must stay saveable on a deployment that stored the old switches");
        repository.Verify(
            r => r.UpsertAsync(
                It.Is<SystemSettingsOverride>(o => o.SectionKey == "TwoFactor" && o.OverridesJson == """{"ReauthenticationMaxAgeMinutes":20}"""),
                It.IsAny<byte[]?>(),
                It.IsAny<CancellationToken>()),
            Times.Once(),
            "the save replaces the whole row, so the retired keys are gone from it");
    }

    [Fact]
    public void TheRetiredOutcomeNumber_IsNotReused()
    {
        // LoginCommitOutcome 5 meant "a reused step let through by the switch". Its
        // number stays retired: a later outcome takes the next free one.
        Enum.GetValues<Auth.Domain.Enums.LoginCommitOutcome>().Select(outcome => (int)outcome).Should().NotContain(5);
    }

    // Adds an already-loaded provider to a configuration builder as it is.
    private sealed class ProviderSource(IConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }
}
