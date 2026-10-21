using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.SecretManagement;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Security;

/// <summary>
/// S11 KR7: the keyring's posture, computed from the ring the secrets file holds, the ring
/// this process holds and the live webhook keys, never from a setting (D-S11B-1).
/// </summary>
public sealed class KeyringPostureContributorTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public KeyringPostureContributorTests()
    {
        // Stand the clock still, so day counts are exact.
        _keyring.Clock.Advance(TimeSpan.Zero);
    }

    public void Dispose() => _keyring.Dispose();

    private KeyringPostureContributor Contributor(KeyringTestHarness? harness = null)
    {
        var h = harness ?? _keyring;
        var scopes = new ServiceCollection()
            .AddSingleton(h.WebhookKeys.Object)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new KeyringPostureContributor(h.Secrets, h.Holder, h.StorageMode, scopes, h.Clock);
    }

    private async Task<SecurityPosture> EvaluateAsync() => await Contributor().EvaluateAsync(CancellationToken.None);

    private static SecurityPostureLine Line(SecurityPosture posture, string item) =>
        posture.Lines.Should().ContainSingle(line => line.MessageTemplate.StartsWith(item + ":", StringComparison.Ordinal)).Which;

    [Fact]
    public async Task BeforeAnyRotation_SaysSo_AtInformation()
    {
        var posture = await EvaluateAsync();

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        Line(posture, "keyring.legacy-derivation").Level.Should().Be(LogLevel.Information);
        Line(posture, "keyring.legacy-derivation").MessageTemplate.Should().Contain("no rotation yet");
    }

    [Fact]
    public async Task AfterTheCutover_CountsTheDaysTheLegacyFormIsStillAccepted()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        _keyring.Clock.Advance(TimeSpan.FromDays(2));

        var posture = await EvaluateAsync();

        var line = Line(posture, "keyring.legacy-derivation");
        line.Level.Should().Be(LogLevel.Warning);
        line.Arguments[1].Should().Be(5, "the shortest window is seven days, and two have passed");
        posture.Level.Should().Be(SecurityPostureLevel.Weak);
    }

    [Fact]
    public async Task AFileAndAProcessThatDisagreeOnTheCutover_AreCritical()
    {
        // Another worker promoted: the file records a cutover this process never saw.
        await _keyring.StageAsync();
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                var ring = HmacKeyringFile.Read(secrets).Value;
                HmacKeyringFile.Write(ring.Promote(_keyring.Clock.UtcNow, _keyring.Clock.UtcNow.AddDays(7)).Value, secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var posture = await EvaluateAsync();

        Line(posture, "keyring.legacy-derivation").Level.Should().Be(LogLevel.Critical);
    }

    [Fact]
    public async Task WebhookKeysUnderAnOldKey_AreAWarning_AndUnderACompromisedKey_Critical()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        _keyring.WebhookCounts = new Dictionary<int, int> { [1] = 3, [2] = 4 };

        var posture = await EvaluateAsync();
        Line(posture, "keyring.webhook-keys-on-old-key").Level.Should().Be(LogLevel.Warning);
        Line(posture, "keyring.webhook-keys-on-old-key").Arguments[0].Should().Be(3, "key 2 issues: its webhook keys are current");

        var emergency = await _keyring.Service.ChangeAsync(
            _keyring.Request(KeyringAction.PromoteEmergency, _keyring.Approve(KeyringAction.PromoteEmergency), material: KeyringTestHarness.KeyMaterial()),
            (ring, change) => ring.PromoteEmergency(change.Material!, change.NowUtc),
            precheck: null,
            CancellationToken.None);
        emergency.IsError.Should().BeFalse();

        var after = await EvaluateAsync();
        Line(after, "keyring.webhook-keys-on-old-key").Level.Should().Be(LogLevel.Critical);
        Line(after, "keyring.webhook-keys-on-old-key").Arguments[0].Should().Be(7);
    }

    [Fact]
    public async Task AKeyStagedMoreThanSevenDays_IsAWarning()
    {
        await _keyring.StageAsync();

        _keyring.Clock.Advance(TimeSpan.FromDays(7));
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.next-staged-too-long", StringComparison.Ordinal));

        _keyring.Clock.Advance(TimeSpan.FromMinutes(1));
        Line(await EvaluateAsync(), "keyring.next-staged-too-long").Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task TheCurrentKey_IsTooOldAt366Days_Not365()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();

        _keyring.Clock.Advance(TimeSpan.FromDays(365));
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal));

        _keyring.Clock.Advance(TimeSpan.FromDays(1));
        Line(await EvaluateAsync(), "keyring.key-age").Arguments.Should().Equal(2, 366);
    }

    [Fact]
    public async Task Key1MigratedFromTheSingleKey_HasNoAge_SoNoKeyAgeWarning()
    {
        _keyring.Clock.Advance(TimeSpan.FromDays(4000));
        var migrated = _keyring.ReadFileRing().Current;
        migrated.CreatedAtUtc.Should().NotBeNull("a new file writes key 1 with its creation time");

        // The single key of a file that never had a ring: key 1 in memory, age unknown.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                secrets.HmacKeys = new Dictionary<int, HmacKeyEntry>();
                secrets.HmacCurrentKeyId = 0;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APreviousKeyThatVerifiesNothingAnyMore_IsAWarningToRetireIt()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();

        _keyring.Clock.Advance(TimeSpan.FromDays(91));

        Line(await EvaluateAsync(), "keyring.previous-past-window").Arguments.Should().Equal("1");
    }

    [Fact]
    public async Task PlainText_SaysTheKeyringCannotBeRotated()
    {
        using var plain = new KeyringTestHarness(plainText: true);

        var posture = await Contributor(plain).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().ContainSingle().Which.MessageTemplate.Should().Contain("cannot be rotated");
    }

    [Theory]
    [InlineData(true, LogLevel.Warning, SecurityPostureLevel.Weak)]
    [InlineData(false, LogLevel.Information, SecurityPostureLevel.Ok)]
    public async Task TheAdminApiSwitch_IsAWarningWhileItIsOn(bool enabled, LogLevel level, SecurityPostureLevel posture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Options.IOptionsSnapshot<SecretManagementSettings>>(
            TestHelpers.CreateOptions(new SecretManagementSettings { EnableAdminApi = enabled }));
        var contributor = new AdminApiPostureContributor(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var result = await contributor.EvaluateAsync(CancellationToken.None);

        result.Level.Should().Be(posture);
        result.Lines.Should().ContainSingle().Which.Level.Should().Be(level);
        result.State.Should().Be(enabled ? "on" : "off");
    }
}
