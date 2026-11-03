using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Security.Keyring;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Configuration;
using ErrorOr;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S12 TS1, TS4 (KR1, KR2, KR4, KR8): the signing keyring's order and windows, and the rings the
/// start refuses, read from a real secrets file through the real configuration provider, as the
/// "key material validation" step reads them.
/// </summary>
public sealed class SigningKeyRingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    // --- what the ring accepts and publishes ---

    [Fact]
    public void Published_IsTheCurrentKey_ThenTheStagedKey_ThenPreviousKeys_TheMostRecentlyDemotedFirst()
    {
        var (current, next, older, newer) = (New(), New(Now.AddDays(-1)), New(), New());
        var ring = Ring(
            [
                current,
                next with { PublishedAtUtc = Now.AddHours(-2) },
                older with { VerifyUntilUtc = Now.AddHours(1) },
                newer with { VerifyUntilUtc = Now.AddHours(20) }
            ],
            current.Kid,
            next.Kid);

        ring.Published(Now).Select(key => key.Kid).Should().Equal(current.Kid, next.Kid, newer.Kid, older.Kid);
    }

    [Fact]
    public void AnExpiredPrevious_ACompromisedKey_AndAStagedKeyPast30Days_AreNeitherAcceptedNorPublished()
    {
        var (current, next, expired, compromised, live) = (New(), New(Now.AddDays(-40)), New(), New(), New());
        var ring = Ring(
            [
                current,
                next with { PublishedAtUtc = Now.AddDays(-31) },
                expired with { VerifyUntilUtc = Now.AddSeconds(-1) },
                compromised with { VerifyUntilUtc = Now.AddDays(1), Compromised = true },
                live with { VerifyUntilUtc = Now.AddSeconds(1) }
            ],
            current.Kid,
            next.Kid);

        ring.Published(Now).Select(key => key.Kid).Should().Equal(current.Kid, live.Kid);
        ring.Verifies(ring.Find(expired.Kid)!, Now).Should().BeFalse("a previous key is accepted until its window ends, not after");
        ring.Verifies(ring.Find(compromised.Kid)!, Now).Should().BeFalse("a compromised key is closed whatever its dates");
        ring.Verifies(ring.Find(next.Kid)!, Now).Should().BeFalse("a staged key is accepted for 30 days from its publication");
        ring.Verifies(ring.Find(next.Kid)!, Now.AddDays(-2)).Should().BeTrue();
    }

    [Fact]
    public void TwoPreviousKeys_AreBothAcceptedUntilTheirOwnWindowsEnd()
    {
        // P4-R3: a second promote inside the first one's window. The file holds both previous
        // keys; nothing depends on the backup copy.
        var ring = Ring([New()], null, null);
        var first = ring.Stage(New(), Now).Value.WithNextPublishedAt(Now).Promote(Now.AddDays(1), Now.AddDays(2)).Value;
        var second = first.Stage(New(), Now.AddDays(1)).Value.WithNextPublishedAt(Now.AddDays(1)).Promote(Now.AddDays(1.5), Now.AddDays(2.5)).Value;

        second.Published(Now.AddDays(1.75)).Should().HaveCount(3);
        second.Published(Now.AddDays(2.25)).Should().HaveCount(2, "the first previous key's window ended");
        second.Keys.Where(key => !second.IsCurrent(key)).Should().OnlyContain(key => key.PrivateKeyPem == null,
            "a key that stopped signing keeps its public key only (KR1)");
    }

    // --- the start ---

    [Fact]
    public void AFileWithoutARing_IsTheMigratedKey_UnderJwtKeyId_NotYetPersisted()
    {
        RemoveTheRing();

        var loaded = Load(keyId: "custom-key-7");

        loaded.Ring.Current.Kid.Should().Be("custom-key-7", "the key keeps the name it has always been published under (TS1)");
        loaded.Ring.Persisted.Should().BeFalse();
        loaded.Ring.Current.IsMigrated.Should().BeTrue();
        loaded.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().Equal("custom-key-7");
        loaded.Current.Key.KeyId.Should().Be("custom-key-7");
    }

    [Fact]
    public void TheMigratedKey_IsPublishedUnderJwtKeyIdAsItIs_AndUnderAuthKey1WhenItNamesNone()
    {
        RemoveTheRing();

        Load(keyId: "custom-key-7 ").PublishedKeys(DateTimeOffset.UtcNow).Should().ContainSingle().Which.KeyId.Should().Be(
            "custom-key-7 ", "the single-key build published Jwt:KeyId untrimmed, so a trim would rename the key at deploy (TS1)");
        Load(keyId: " ").Source.MigratedKid.Should().Be(RsaKeyring.MigratedKeyId);
    }

    [Fact]
    public void TheStart_RefusesAJwtKeyIdThatCannotNameTheMigratedKey_NamingTheSettingAndItsRule()
    {
        RemoveTheRing();

        var act = () => Load(keyId: "a:b");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:KeyId 'a:b' cannot name the signing key*at most 128 characters and holds no ':'*")
            .Which.Message.Should().NotContain("secrets file", "the file is not at fault, and no copy of it would help");
    }

    [Fact]
    public void ACompromisedRecord_Boots_AndIsNeitherAcceptedNorPublished()
    {
        var compromised = New();
        WriteRing(ring => ring.With(compromised with { VerifyUntilUtc = Now, Compromised = true, PrivateKeyPem = null }));

        var loaded = Load();

        loaded.Ring.Find(compromised.Kid)!.Compromised.Should().BeTrue();
        loaded.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().NotContain(compromised.Kid);
        loaded.VerificationKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().NotContain(compromised.Kid);
    }

    [Fact]
    public void TheStart_RefusesTheStagedKeyBeingTheCurrentOne()
    {
        WriteRaw(secrets => secrets.SigningNextKid = secrets.SigningCurrentKid);

        Refusal().Should().Contain("both the current and the staged key");
    }

    [Fact]
    public void TheStart_RefusesAKidThatIsNotItsKeysThumbprint()
    {
        WriteRaw(secrets =>
        {
            var current = secrets.SigningKeys[secrets.SigningCurrentKid!];
            secrets.SigningKeys.Remove(secrets.SigningCurrentKid!);
            secrets.SigningKeys["not-its-thumbprint"] = current;
            secrets.SigningCurrentKid = "not-its-thumbprint";
        });

        Refusal().Should().Contain("is not the thumbprint of its key");
    }

    [Fact]
    public void TheStart_RefusesASingleKeyThatIsNotTheCurrentKey()
    {
        WriteRaw(secrets => secrets.JwtPrivateKeyPem = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair().PrivateKeyPem);

        Refusal().Should().Contain("is not the keyring's current key");
    }

    [Fact]
    public void TheStart_RefusesAStagedKeyWithoutItsPrivateKey()
    {
        var staged = New();
        WriteRaw(secrets =>
        {
            secrets.SigningKeys[staged.Kid] = new SigningKeyEntry { PublicKeyPem = staged.PublicKeyPem, CreatedAtUtc = Now };
            secrets.SigningNextKid = staged.Kid;
        });

        Refusal().Should().Contain("has no private key");
    }

    [Fact]
    public void TheFileReader_RefusesTwoKeysUnderOneKidThatDifferOnlyInCase()
    {
        // Configuration keys compare without case, so two such kids would collapse into one
        // there; the file reader refuses them where the file still tells them apart.
        var key = New();
        var lower = key with { Kid = key.Kid.ToLowerInvariant() == key.Kid ? key.Kid.ToUpperInvariant() : key.Kid.ToLowerInvariant() };
        var entries = new Dictionary<string, SigningKeyEntry>(StringComparer.Ordinal)
        {
            [key.Kid] = Entry(key),
            [lower.Kid] = Entry(New())
        };

        var ring = RsaKeyringFile.FromEntries(entries, key.Kid, null, key.PrivateKeyPem!, [], "auth-key-1");

        ring.FirstError.Description.Should().Contain("Two signing keys are named");
    }

    [Fact]
    public void TheStart_RefusesAKeyringValueFromAnotherSource()
    {
        // As for the HMAC keyring (S11 SEC-1): only the encrypted file may name signing keys.
        var configuration = new ConfigurationBuilder()
            .AddDpapiSecrets(_keyring.Protection, _keyring.FilePath)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Keyring:Rsa:NextKid"] = "planted" })
            .Build();

        var act = () => SigningKeyRingConfiguration.Load(configuration, Settings(configuration), null, plainText: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*may only come from the encrypted secrets file*Keyring:Rsa:NextKid*");
    }

    // --- helpers ---

    private static SigningKey New(DateTime? createdAtUtc = null)
    {
        var key = KeyringTestHarness.NewSigningKey();
        return key with { CreatedAtUtc = createdAtUtc ?? Now.AddDays(-3) };
    }

    private static RsaKeyring Ring(IReadOnlyList<SigningKey> keys, string? currentKid, string? nextKid) =>
        RsaKeyring.Create(keys, currentKid ?? keys[0].Kid, nextKid, removed: null).Value;

    private static SigningKeyEntry Entry(SigningKey key) => new()
    {
        PrivateKeyPem = key.PrivateKeyPem,
        PublicKeyPem = key.PublicKeyPem,
        CreatedAtUtc = key.CreatedAtUtc,
        PublishedAtUtc = key.PublishedAtUtc,
        VerifyUntilUtc = key.VerifyUntilUtc,
        Compromised = key.Compromised
    };

    private SigningKeyRing Load(string keyId = "auth-key-1")
    {
        var configuration = new ConfigurationBuilder().AddDpapiSecrets(_keyring.Protection, _keyring.FilePath).Build();
        var settings = Settings(configuration);
        settings.KeyId = keyId;
        return SigningKeyRingConfiguration.Load(configuration, settings, null, plainText: false);
    }

    private static JwtSettings Settings(IConfiguration configuration) =>
        configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

    private string Refusal()
    {
        var act = () => Load();
        return act.Should().Throw<InvalidOperationException>().Which.Message;
    }

    private void RemoveTheRing() => _keyring.RemoveSigningRing();

    private void WriteRing(Func<RsaKeyringBuilder, RsaKeyringBuilder> edit) => WriteRaw(secrets =>
    {
        foreach (var key in edit(new RsaKeyringBuilder()).Keys)
        {
            secrets.SigningKeys[key.Kid] = Entry(key);
        }
    });

    private void WriteRaw(Action<SecretConfiguration> edit) =>
        _keyring.Secrets.UpdateAsync<Success>(
                secrets =>
                {
                    edit(secrets);
                    return Result.Success;
                },
                overwriteBackup: false,
                CancellationToken.None)
            .GetAwaiter().GetResult();

    /// <summary>Keys to add to the file's ring as they are.</summary>
    private sealed class RsaKeyringBuilder
    {
        public List<SigningKey> Keys { get; } = [];

        public RsaKeyringBuilder With(SigningKey key)
        {
            Keys.Add(key);
            return this;
        }
    }
}
