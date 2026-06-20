using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Tests for the Argon2id pepper (KnownSecret) support and the transparent keyid-based migration.
/// </summary>
public class Argon2PasswordHasherPepperTests
{
    private const string Password = "Admin@123!";
    private static readonly string PepperKey1 = Convert.ToBase64String(MakeBytes(32, 1));
    private static readonly string PepperKey2 = Convert.ToBase64String(MakeBytes(32, 99));

    private static byte[] MakeBytes(int length, byte seed)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(seed + i);
        }
        return bytes;
    }

    private static Argon2PasswordHasher CreateHasher(
        bool pepperEnabled,
        int currentKeyId = 0,
        Dictionary<int, string>? keys = null)
    {
        var settings = new PasswordSettings
        {
            Argon2MemorySize = 19456,
            Argon2Iterations = 2,
            Argon2Parallelism = 1,
            SaltSize = 16,
            HashSize = 32,
            Pepper = new PepperSettings
            {
                Enabled = pepperEnabled,
                CurrentKeyId = currentKeyId,
                Keys = keys ?? new Dictionary<int, string>()
            }
        };

        return new Argon2PasswordHasher(Options.Create(settings));
    }

    [Fact]
    public void HashPassword_WithoutPepper_ProducesNoKeyidAndVerifies()
    {
        var hasher = CreateHasher(pepperEnabled: false);

        var hash = hasher.HashPassword(Password);

        hash.Should().StartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        hash.Should().NotContain("keyid");
        hasher.VerifyPassword(Password, hash).Should().BeTrue();
        hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void HashPassword_WithPepper_EmitsKeyidAndVerifies()
    {
        var hasher = CreateHasher(true, 1, new() { [1] = PepperKey1 });

        var hash = hasher.HashPassword(Password);

        hash.Should().Contain(",keyid=1$");
        hasher.VerifyPassword(Password, hash).Should().BeTrue();
        hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void LegacyHash_VerifiesUnderPepperEnabled_AndIsFlaggedForRehash()
    {
        // A hash created before peppering (no keyid).
        var legacyHash = CreateHasher(pepperEnabled: false).HashPassword(Password);

        var peppered = CreateHasher(true, 1, new() { [1] = PepperKey1 });

        peppered.VerifyPassword(Password, legacyHash).Should().BeTrue();
        peppered.NeedsRehash(legacyHash).Should().BeTrue(); // upgrade to peppered on next login
    }

    [Fact]
    public void WrongPepper_FailsVerification()
    {
        var hash = CreateHasher(true, 1, new() { [1] = PepperKey1 }).HashPassword(Password);

        var differentPepper = CreateHasher(true, 1, new() { [1] = PepperKey2 });

        differentPepper.VerifyPassword(Password, hash).Should().BeFalse();
    }

    [Fact]
    public void MissingPepperForKeyid_FailsClosed()
    {
        var hash = CreateHasher(true, 1, new() { [1] = PepperKey1 }).HashPassword(Password);

        // Hasher that no longer holds the pepper for keyid 1 (the "lost pepper" scenario).
        var withoutPepper = CreateHasher(pepperEnabled: false);

        withoutPepper.VerifyPassword(Password, hash).Should().BeFalse();
    }

    [Fact]
    public void Rotation_OldHashStillVerifies_NewHashUsesNewKeyId()
    {
        var hashV1 = CreateHasher(true, 1, new() { [1] = PepperKey1 }).HashPassword(Password);

        // Rotate: current key id is now 2, but the old pepper is retained for verification.
        var rotated = CreateHasher(true, 2, new() { [1] = PepperKey1, [2] = PepperKey2 });

        rotated.VerifyPassword(Password, hashV1).Should().BeTrue();
        rotated.NeedsRehash(hashV1).Should().BeTrue();

        var hashV2 = rotated.HashPassword(Password);
        hashV2.Should().Contain(",keyid=2$");
        rotated.NeedsRehash(hashV2).Should().BeFalse();
    }

    [Fact]
    public void PepperedHash_FlaggedForRehash_WhenPepperingDisabled()
    {
        var pepperedHash = CreateHasher(true, 1, new() { [1] = PepperKey1 }).HashPassword(Password);

        // Disabled, but still holds the pepper key so it can verify and migrate away.
        var disabled = CreateHasher(false, 0, new() { [1] = PepperKey1 });

        disabled.VerifyPassword(Password, pepperedHash).Should().BeTrue();
        disabled.NeedsRehash(pepperedHash).Should().BeTrue(); // wants keyid 0 (none)
    }

    [Fact]
    public void EnabledWithoutCurrentPepper_ThrowsAtConstruction()
    {
        var act = () => CreateHasher(pepperEnabled: true, currentKeyId: 1, keys: new Dictionary<int, string>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SeededAdminHash_StillVerifies()
    {
        // Regression guard: the hash seeded into the database for Admin@123! must keep verifying.
        const string seededHash =
            "$argon2id$v=19$m=19456,t=2,p=1$NoKP1nsfZyPf3Hp_V4IHww$_zyvdZiGmyfs87h7_q2f3A.VzxgOfnKVmL5doZ3Kz5Y";

        CreateHasher(pepperEnabled: false).VerifyPassword("Admin@123!", seededHash).Should().BeTrue();
    }
}
