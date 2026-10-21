using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auth.Application.Configuration;
using Auth.Application.Security.Keyring;
using Auth.Infrastructure.Security;
using ErrorOr;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecretFile = Auth.Application.Configuration.SecretConfiguration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 TS7 (PR A half; KR1): the HMAC keyring lives in the secrets file, which other
/// operations save too (SMTP password, connection string). Every save keeps the ring;
/// the single key always mirrors the ring's current key; and a file where the two
/// disagree — saved by a build without the keyring after the ring changed — stops
/// the boot with the way out, instead of issuing under one key and verifying under
/// the other. PR B's half: a keyring change is one locked read-modify-write, so an SMTP
/// save that races it can neither lose it nor be lost; a hand-edited null ring never
/// stops an unrelated save; and the ring a change saves is the ring the next boot loads.
/// </summary>
public class SecretFileKeyringRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-keyring-tests", Guid.NewGuid().ToString("N"));
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly DpapiSecretService _service;

    public SecretFileKeyringRoundTripTests()
    {
        Directory.CreateDirectory(_directory);
        _service = new DpapiSecretService(
            _protection,
            Options.Create(new SecretManagementSettings { SecretFilePath = FilePath, AutoGenerateKeys = true }),
            NullLogger<DpapiSecretService>.Instance);
    }

    private string FilePath => Path.Combine(_directory, "secrets.dpapi");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnSmtpSave_AfterTheRingIsInTheFile_KeepsEveryKey()
    {
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        secrets.HmacKeys[2] = new Auth.Application.Configuration.HmacKeyEntry
        {
            Material = KeyMaterialGenerator.GenerateHmacKeyBase64(),
            CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        secrets.HmacNextKeyId = 2;
        await _service.SaveSecretsAsync(secrets, CancellationToken.None);

        await _service.SetSecretAsync("SmtpPassword", "a-new-smtp-password", CancellationToken.None);

        var after = await _service.LoadSecretsAsync(CancellationToken.None);
        after.SmtpPassword.Should().Be("a-new-smtp-password");
        after.HmacKeys.Keys.Should().BeEquivalentTo([1, 2]);
        after.HmacKeys[2].Material.Should().Be(secrets.HmacKeys[2].Material);
        after.HmacKeys[2].CreatedAtUtc.Should().Be(secrets.HmacKeys[2].CreatedAtUtc);
        after.HmacCurrentKeyId.Should().Be(1);
        after.HmacNextKeyId.Should().Be(2);
    }

    [Fact]
    public async Task EverySave_MirrorsTheCurrentKey_IntoTheSingleKey()
    {
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        var rewritten = KeyMaterialGenerator.GenerateHmacKeyBase64();
        secrets.HmacKeys[1].Material = rewritten;

        await _service.SaveSecretsAsync(secrets, CancellationToken.None);

        var after = await _service.LoadSecretsAsync(CancellationToken.None);
        after.RefreshTokenHmacKey.Should().Be(rewritten);
        after.HmacKeys[1].Material.Should().Be(rewritten);
    }

    [Fact]
    public async Task AKeyringChange_IsTheRingTheNextBootLoads()
    {
        // The two readers of the ring agree: what a transition reads back from the file
        // model (HmacKeyringFile) is what the startup step loads through the provider.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var now = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

        var saved = await _service.UpdateAsync<HmacKeyring>(
            secrets =>
            {
                var promoted = HmacKeyringFile.Read(secrets).Value
                    .Stage(Convert.FromBase64String(KeyMaterialGenerator.GenerateHmacKeyBase64()), now).Value
                    .Promote(now.AddDays(1), now.AddDays(31)).Value;
                HmacKeyringFile.Write(promoted, secrets);
                return HmacKeyringFile.Read(secrets);
            },
            overwriteBackup: false,
            CancellationToken.None);

        var booted = HmacKeyringConfiguration.Load(LoadConfiguration());
        booted.Current.Id.Should().Be(2);
        HmacKeyringFile.SameIssuingState(saved.Value, booted).Should().BeTrue();
        booted.Find(1)!.DemotedAtUtc.Should().Be(now.AddDays(1));
        booted.Find(1)!.VerifyUntilUtc.Should().Be(now.AddDays(31));
        booted.LegacyVerifyUntilUtc.Should().Be(now.AddDays(31));
        booted.Find(2)!.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task AnSmtpSave_RacingAKeyringPromote_KeepsBothChanges()
    {
        // The SMTP save loads the file, and is held right there, inside its load; the
        // promote starts while it is held. A load and a save under two lock acquisitions
        // let the promote run in between and the SMTP save write the old ring back over
        // it. One acquisition across both makes the promote wait for the SMTP save.
        var gate = new GatedProtectionProvider();
        var service = new DpapiSecretService(
            gate,
            Options.Create(new SecretManagementSettings { SecretFilePath = FilePath, AutoGenerateKeys = true }),
            NullLogger<DpapiSecretService>.Instance);
        await service.GenerateMissingKeysAsync(CancellationToken.None);
        var now = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
        await service.UpdateAsync<Success>(
            secrets =>
            {
                HmacKeyringFile.Write(
                    HmacKeyringFile.Read(secrets).Value
                        .Stage(Convert.FromBase64String(KeyMaterialGenerator.GenerateHmacKeyBase64()), now).Value,
                    secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        gate.HoldNextLoad();
        var smtp = Task.Run(() => service.SetSecretAsync("SmtpPassword", "raced-smtp-password", CancellationToken.None));
        await gate.Held.WaitAsync(TimeSpan.FromSeconds(10));

        var promote = Task.Run(() => service.UpdateAsync<Success>(
            secrets =>
            {
                HmacKeyringFile.Write(HmacKeyringFile.Read(secrets).Value.Promote(now, now.AddDays(7)).Value, secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        gate.Release();
        await Task.WhenAll(smtp, promote).WaitAsync(TimeSpan.FromSeconds(10));

        var after = await service.LoadSecretsAsync(CancellationToken.None);
        after.SmtpPassword.Should().Be("raced-smtp-password");
        after.HmacCurrentKeyId.Should().Be(2, "the promote that started while the SMTP save was loading must not be lost");
        after.HmacLegacyCutoverUtc.Should().Be(now);
    }

    [Theory]
    [InlineData("null ring")]
    [InlineData("null entry")]
    public async Task AHandEditedNullRing_DoesNotStopAnUnrelatedSave(string shape)
    {
        // S11 SEC-3: "hmacKeys": null, or a key whose entry is null, must not make an
        // SMTP save throw; the boot step is where a broken ring is reported.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        await _service.UpdateAsync<Success>(
            secrets =>
            {
                if (shape == "null ring")
                {
                    secrets.HmacKeys = null!;
                }
                else
                {
                    secrets.HmacKeys[2] = null!;
                }

                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var save = () => _service.SetSecretAsync("SmtpPassword", "still-saves", CancellationToken.None);

        await save.Should().NotThrowAsync();
        (await _service.LoadSecretsAsync(CancellationToken.None)).SmtpPassword.Should().Be("still-saves");
    }

    [Fact]
    public async Task TheProvider_MapsTheRing_AndTheStartupStepLoadsItBack()
    {
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        var demotedAt = new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc);
        secrets.HmacKeys[1].DemotedAtUtc = demotedAt;
        secrets.HmacKeys[2] = new Auth.Application.Configuration.HmacKeyEntry { Material = KeyMaterialGenerator.GenerateHmacKeyBase64(), CreatedAtUtc = demotedAt };
        secrets.HmacCurrentKeyId = 2;
        secrets.HmacLegacyCutoverUtc = demotedAt;
        await _service.SaveSecretsAsync(secrets, CancellationToken.None);

        var configuration = LoadConfiguration();
        configuration["Keyring:Hmac:CurrentKeyId"].Should().Be("2");
        configuration["Jwt:RefreshTokenHmacKeyPlain"].Should().Be(secrets.HmacKeys[2].Material, "the single key mirrors the current one");

        var ring = HmacKeyringConfiguration.Load(configuration);
        ring.Current.Id.Should().Be(2);
        ring.LegacyCutoverUtc.Should().Be(demotedAt);
        ring.Find(1)!.DemotedAtUtc.Should().Be(demotedAt);
        ring.Issuing.IsLegacy.Should().BeFalse();
    }

    [Fact]
    public async Task AFileWhoseSingleKeyIsNotTheCurrentKey_RefusesTheBoot_NamingTheWayOut()
    {
        // Every save mirrors the current key into the single key, so this state comes only
        // from a hand edit or a restore that mixed two copies (forged here by writing the
        // JSON directly, past the service's mirror).
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        secrets.RefreshTokenHmacKey = KeyMaterialGenerator.GenerateHmacKeyBase64();
        WriteWithoutMirroring(secrets);

        var load = () => HmacKeyringConfiguration.Load(LoadConfiguration());

        load.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not the HMAC keyring's current key 1*Restore the most recent copy of the secrets file*")
            .And.Message.Should().NotContain(secrets.RefreshTokenHmacKey, "no key material in a log line");
    }

    [Fact]
    public async Task ASaveByABuildWithoutTheKeyring_AfterARotation_RefusesTheBoot()
    {
        // Rolled back past S11 after key 2 took over: the old build's first save erases the
        // ring and keeps the single key (key 2's material). Rolled forward, starting on it as
        // "key 1" would issue under the wrong key and orphan every key-2 fingerprint. The
        // file's version, which the old build carries through, says a ring existed.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        secrets.HmacKeys[1].DemotedAtUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        secrets.HmacKeys[2] = new Auth.Application.Configuration.HmacKeyEntry { Material = KeyMaterialGenerator.GenerateHmacKeyBase64() };
        secrets.HmacCurrentKeyId = 2;
        secrets.HmacLegacyCutoverUtc = secrets.HmacKeys[1].DemotedAtUtc;
        await _service.SaveSecretsAsync(secrets, CancellationToken.None);
        (await _service.LoadSecretsAsync(CancellationToken.None)).Version.Should().Be(SecretFileVersions.HmacKeyringBeyondKey1);

        SaveAsABuildWithoutTheKeyring();

        var load = () => HmacKeyringConfiguration.Load(LoadConfiguration());
        load.Should().Throw<InvalidOperationException>()
            .WithMessage("*had an HMAC keyring beyond key 1 and no longer has one*Restore the most recent copy*");
    }

    [Fact]
    public async Task ASaveByABuildWithoutTheKeyring_OfAKey1OnlyRing_StillBoots()
    {
        // A file this PR wrote holds key 1 alone, so the old build erasing it loses nothing:
        // the single key IS key 1. A rollback and a roll-forward must keep booting.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var single = (await _service.LoadSecretsAsync(CancellationToken.None)).RefreshTokenHmacKey!;

        SaveAsABuildWithoutTheKeyring();

        var ring = HmacKeyringConfiguration.Load(LoadConfiguration());
        ring.Current.Id.Should().Be(1);
        ring.Current.Material.ToArray().Should().Equal(Convert.FromBase64String(single));
    }

    [Fact]
    public async Task TheFileVersion_RisesOnlyForAnHmacRingBeyondKey1()
    {
        // Version >= 2 means exactly "an HMAC ring beyond key 1 existed" (SecretFileVersions):
        // no other save raises it, the RSA ring of S12 included, or the boot check above
        // would refuse files it should not.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        await _service.SetSecretAsync("SmtpPassword", "a-new-smtp-password", CancellationToken.None);
        var secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        secrets.SigningKeys["kid-1"] = new Auth.Application.Configuration.SigningKeyEntry { PublicKeyPem = "public" };
        secrets.SigningCurrentKid = "kid-1";
        await _service.SaveSecretsAsync(secrets, CancellationToken.None);
        await _service.UpdateAsync<Success>(
            file =>
            {
                // Key 1 rewritten in place, as the pre-keyring generate did: still key 1 only.
                file.HmacKeys[1].Material = KeyMaterialGenerator.GenerateHmacKeyBase64();
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        (await _service.LoadSecretsAsync(CancellationToken.None)).Version.Should().Be(SecretFileVersions.Initial);

        secrets = await _service.LoadSecretsAsync(CancellationToken.None);
        secrets.HmacKeys[2] = new Auth.Application.Configuration.HmacKeyEntry { Material = KeyMaterialGenerator.GenerateHmacKeyBase64() };
        secrets.HmacNextKeyId = 2;
        await _service.SaveSecretsAsync(secrets, CancellationToken.None);

        (await _service.LoadSecretsAsync(CancellationToken.None)).Version.Should().Be(SecretFileVersions.HmacKeyringBeyondKey1);
    }

    [Fact]
    public void AnEmptyPlaceholderKey_IsNoKey()
    {
        // The empty-string placeholder settings files use for "not set" must not stop the
        // boot (the CI check accepts it too).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenHmacKeyPlain"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["Keyring:Hmac:Keys:1:Material"] = "",
                ["Keyring:Hmac:Keys:1:Compromised"] = ""
            })
            .Build();

        var ring = HmacKeyringConfiguration.Load(configuration);

        ring.Keys.Should().ContainSingle().Which.Id.Should().Be(1);
        ring.Current.CreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void AnEmptyRing_IsTheSingleKey_AsKey1_InMemory()
    {
        var single = KeyMaterialGenerator.GenerateHmacKeyBase64();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:RefreshTokenHmacKeyPlain"] = single })
            .Build();

        var ring = HmacKeyringConfiguration.Load(configuration);

        ring.Keys.Should().ContainSingle().Which.Id.Should().Be(1);
        ring.Current.CreatedAtUtc.Should().BeNull("a migrated key 1's age is unknown");
        ring.Issuing.IsLegacy.Should().BeTrue();
        File.Exists(FilePath).Should().BeFalse("the migration happens in memory; nothing is written");
    }

    [Theory]
    [InlineData("Keyring:Hmac:CurrentKeyId", "1", "but no keys")]
    [InlineData("Keyring:Hmac:Keys:1:Material", "c2hvcnQ=", "at least 32 bytes")]
    [InlineData("Keyring:Hmac:Keys:x:Material", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", "invalid id")]
    public void AMalformedRing_RefusesTheBoot(string key, string value, string reason)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenHmacKeyPlain"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                [key] = value
            })
            .Build();

        var load = () => HmacKeyringConfiguration.Load(configuration);

        load.Should().Throw<InvalidOperationException>().WithMessage($"*{reason}*");
    }

    private IConfiguration LoadConfiguration() =>
        new ConfigurationBuilder().AddDpapiSecrets(_protection, FilePath).Build();

    /// <summary>
    /// What a save by a build without the keyring does to the file: its model has no
    /// keyring members and no extension data, so they are dropped; everything it knows,
    /// Version included, is written back as read.
    /// </summary>
    private void SaveAsABuildWithoutTheKeyring()
    {
        var protector = _protection.CreateProtector("AuthSystem.SecretManagement.v1");
        var json = JsonNode.Parse(Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(FilePath))))!.AsObject();
        foreach (var name in json.Select(pair => pair.Key)
                     .Where(name => name.StartsWith("hmac", StringComparison.OrdinalIgnoreCase) || name.StartsWith("signing", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            json.Remove(name);
        }

        File.WriteAllBytes(FilePath, protector.Protect(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    private void WriteWithoutMirroring(SecretFile secrets)
    {
        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = _protection.CreateProtector("AuthSystem.SecretManagement.v1").Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(FilePath, encrypted);
    }

    /// <summary>
    /// Data Protection whose next decryption (a load of the secrets file) waits until the
    /// test releases it, to hold a save at a known point inside its read-modify-write.
    /// </summary>
    private sealed class GatedProtectionProvider : IDataProtectionProvider
    {
        private readonly EphemeralDataProtectionProvider _inner = new();
        private volatile bool _armed;
        private readonly ManualResetEventSlim _release = new(false);

        public SemaphoreSlim Held { get; } = new(0, 1);

        public void HoldNextLoad() => _armed = true;

        public void Release() => _release.Set();

        public IDataProtector CreateProtector(string purpose) => new Gated(this, _inner.CreateProtector(purpose));

        private sealed class Gated(GatedProtectionProvider owner, IDataProtector inner) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Gated(owner, inner.CreateProtector(purpose));

            public byte[] Protect(byte[] plaintext) => inner.Protect(plaintext);

            public byte[] Unprotect(byte[] protectedData)
            {
                if (owner._armed)
                {
                    owner._armed = false;
                    owner.Held.Release();
                    owner._release.Wait(TimeSpan.FromSeconds(10));
                }

                return inner.Unprotect(protectedData);
            }
        }
    }
}
