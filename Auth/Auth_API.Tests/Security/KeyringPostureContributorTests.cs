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
        return new KeyringPostureContributor(h.Secrets, h.Holder, h.SigningKeys, h.StorageMode, scopes, h.Clock);
    }

    private async Task<SecurityPosture> EvaluateAsync() => await Contributor().EvaluateAsync(CancellationToken.None);

    // The HMAC keyring's line of an item; the signing keyring's lines (S12) share the item names.
    private static SecurityPostureLine Line(SecurityPosture posture, string item) =>
        posture.Lines.Should().ContainSingle(line => line.MessageTemplate.StartsWith(item + ":", StringComparison.Ordinal)
            && !line.MessageTemplate.Contains("signing key", StringComparison.Ordinal)).Which;

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
    public async Task AProcessThatMissedAnotherWorkersEmergency_IsCritical_ThoughItIssuesUnderTheSameKey()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // Another worker marked every key leaked and installed key 3; this process still holds
        // the ring it promoted.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                var ring = HmacKeyringFile.Read(secrets).Value;
                HmacKeyringFile.Write(
                    ring.PromoteEmergency(Convert.FromBase64String(KeyringTestHarness.KeyMaterial()), _keyring.Clock.UtcNow).Value,
                    secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var posture = await EvaluateAsync();

        var line = Line(posture, "keyring.process-ring");
        line.Level.Should().Be(LogLevel.Critical);
        line.Arguments.Should().Equal(2, 3, "1!,2!,3", "1,2");
        posture.Level.Should().Be(SecurityPostureLevel.Weak);
    }

    [Fact]
    public async Task AProcessThatMissedAnotherWorkersRetire_IsCritical_ThoughItsIssuingKeyIsTheFiles()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // Another worker retired key 1: the file and this process issue under key 2, with
        // nothing staged and the same cutover, and still hold different rings.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                HmacKeyringFile.Write(HmacKeyringFile.Read(secrets).Value.Retire(1, _keyring.Clock.UtcNow).Value, secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var posture = await EvaluateAsync();

        Line(posture, "keyring.process-ring").Arguments.Should().Equal(2, 2, "2", "1,2");
    }

    [Fact]
    public async Task AProcessThatHoldsTheFilesRing_HasNoProcessRingLine()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();

        var posture = await EvaluateAsync();

        posture.Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.process-ring:", StringComparison.Ordinal));
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
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("HMAC", StringComparison.Ordinal), "the signing keyring has its own key-age line (S12)");

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

        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("HMAC", StringComparison.Ordinal), "the signing keyring has its own key-age line (S12)");
    }

    [Fact]
    public async Task APreviousKeyThatVerifiesNothingAnyMore_IsAWarningToRetireIt()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();

        _keyring.Clock.Advance(TimeSpan.FromDays(91));

        Line(await EvaluateAsync(), "keyring.previous-past-window").Arguments.Should().Equal("1");
    }

    // --- the signing keyring (S12) ---

    [Fact]
    public async Task ASigningKeyStagedMoreThanSevenDays_IsAWarning_NotAtSeven()
    {
        var staged = await StageSigningKeyAsync();

        _keyring.Clock.Advance(TimeSpan.FromDays(7));
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.next-staged-too-long", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal));

        _keyring.Clock.Advance(TimeSpan.FromMinutes(1));
        var line = SigningLine(await EvaluateAsync(), "keyring.next-staged-too-long");
        line.Level.Should().Be(LogLevel.Warning);
        line.Arguments[0].Should().Be(staged);
    }

    [Fact]
    public async Task TheCurrentSigningKey_IsTooOldAt366Days_NamedByItsKid()
    {
        // The key was written by the harness just before the clock stood still.
        var kid = _keyring.SigningKeys.Current.Key.KeyId;

        _keyring.Clock.Advance(TimeSpan.FromDays(365) - TimeSpan.FromMinutes(1));
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal));

        _keyring.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(2));
        SigningLine(await EvaluateAsync(), "keyring.key-age").Arguments.Should().Equal(kid, 366);
    }

    [Fact]
    public async Task TheMigratedSigningKey_HasNoAge_SoNoKeyAgeWarning()
    {
        // A file that never had a signing ring: the one key, under Jwt:KeyId, of unknown age.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                secrets.SigningKeys = new Dictionary<string, SigningKeyEntry>();
                secrets.SigningCurrentKid = null;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);
        _keyring.Restart();
        _keyring.SigningKeys.Current.Key.KeyId.Should().Be("auth-key-1");

        _keyring.Clock.Advance(TimeSpan.FromDays(4000));

        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.key-age", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProcessSigningWithAKeyTheFileDemoted_IsCritical()
    {
        // Another worker promoted: this process still signs with the key the file calls previous.
        await StageSigningKeyAsync();
        _keyring.Restart();
        var mine = _keyring.SigningKeys.Current.Key.KeyId;
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                var ring = RsaKeyringFile.Read(secrets, "auth-key-1").Value;
                RsaKeyringFile.Write(ring.Promote(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)).Value, secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var line = SigningLine(await EvaluateAsync(), "keyring.process-ring");

        line.Level.Should().Be(LogLevel.Critical);
        line.Arguments[0].Should().Be(mine);
    }

    [Fact]
    public async Task AProcessThatStillAcceptsACompromisedKey_IsCritical_UntilItRestarts()
    {
        // An emergency whose staged key this process never loaded is saved, and waits for the restart.
        var leaked = _keyring.SigningKeys.Current.Key.KeyId;
        await StageSigningKeyAsync();
        (await _keyring.SigningService.ChangeAsync(
            _keyring.SigningRequest(KeyringAction.PromoteEmergency, _keyring.ApproveSigning(KeyringAction.PromoteEmergency)),
            newKey: null,
            (ring, change) => ring.PromoteEmergency(change.NowUtc),
            Auth.Application.Features.Secrets.Common.RsaKeyringChangeService.WithStagedKeyLoaded((ring, change) => ring.PromoteEmergency(change.NowUtc)),
            CancellationToken.None)).Value.ChangeAppliedInProcess.Should().BeFalse();

        (await EvaluateAsync()).Lines.Should().Contain(line => line.Level == LogLevel.Critical
            && line.MessageTemplate.StartsWith("keyring.process-ring", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("marks compromised", StringComparison.Ordinal)
            && Equals(line.Arguments[0], leaked));

        _keyring.Restart();
        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.process-ring", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal), "the restart closed it");
    }

    [Fact]
    public async Task AStagedSigningKeyThisProcessHasNotLoadedYet_IsNotAProcessRingFinding()
    {
        // The normal state between a stage and the restart that loads it.
        await StageSigningKeyAsync();

        (await EvaluateAsync()).Lines.Should().NotContain(line => line.MessageTemplate.StartsWith("keyring.process-ring", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal));
    }

    private async Task<string> StageSigningKeyAsync()
    {
        var staged = await _keyring.SigningService.ChangeAsync(
            _keyring.SigningRequest(KeyringAction.Stage, _keyring.ApproveSigning(KeyringAction.Stage)),
            KeyringTestHarness.NewSigningKey(),
            (ring, change) => ring.Stage(change.NewKey!, change.NowUtc),
            applyInProcess: null,
            CancellationToken.None);
        staged.IsError.Should().BeFalse();
        return staged.Value.NextKid!;
    }

    // The signing keyring's line of an item.
    private static SecurityPostureLine SigningLine(SecurityPosture posture, string item) =>
        posture.Lines.Should().ContainSingle(line => line.MessageTemplate.StartsWith(item + ":", StringComparison.Ordinal)
            && line.MessageTemplate.Contains("signing key", StringComparison.Ordinal)).Which;

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
