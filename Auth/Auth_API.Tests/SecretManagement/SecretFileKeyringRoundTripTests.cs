using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
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
/// the other. The concurrency half needs UpdateAsync and ships with PR B.
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
    public async Task GeneratingTheHmacKey_ReplacesItInBothPlaces_AndTheNextBootAcceptsIt()
    {
        // Today's generate/import keep their meaning (replace the one key); PR B turns them
        // into keyring aliases. Writing only the single key would refuse the next boot.
        await _service.GenerateMissingKeysAsync(CancellationToken.None);
        var before = (await _service.LoadSecretsAsync(CancellationToken.None)).RefreshTokenHmacKey;

        await _service.GenerateHmacKeyAsync(CancellationToken.None);

        var after = await _service.LoadSecretsAsync(CancellationToken.None);
        after.RefreshTokenHmacKey.Should().NotBe(before);
        after.HmacKeys[1].Material.Should().Be(after.RefreshTokenHmacKey);
        var ring = HmacKeyringConfiguration.Load(LoadConfiguration());
        ring.Current.Id.Should().Be(1);
        ring.Current.Material.ToArray().Should().Equal(Convert.FromBase64String(after.RefreshTokenHmacKey!));
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
        // What a build without the keyring writes after the ring moved on: it saves the
        // single key it knows and nothing of the ring (here forged by writing the JSON
        // directly, as that build's serializer would).
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

    private void WriteWithoutMirroring(SecretFile secrets)
    {
        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = _protection.CreateProtector("AuthSystem.SecretManagement.v1").Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(FilePath, encrypted);
    }
}
