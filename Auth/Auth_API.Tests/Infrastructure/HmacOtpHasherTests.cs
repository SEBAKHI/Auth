using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Confirmation codes are keyed-hashed rather than password-hashed.
/// </summary>
/// <remarks>
/// A six-digit code lives five minutes and dies after five wrong guesses, so
/// making each guess slow buys nothing — a million candidates at the password
/// cost takes about fifteen hours, and the code has been dead for fourteen of
/// them. What protects it is a key that is not in the database. The change also
/// removes one of the two password-grade hashes registration paid on a single
/// request, which is what capped how many accounts a server could create per
/// second.
/// </remarks>
public class HmacOtpHasherTests
{
    private const string LegacyArgon2Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA";

    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly HmacOtpHasher _hasher;

    public HmacOtpHasherTests()
    {
        var keyService = new Mock<IRefreshTokenKeyService>();

        // A real keyed hash over the message the implementation builds, so the
        // assertions below are about the composition rather than about a stub
        // that agrees with itself.
        var key = Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-bytes-long!");
        keyService
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns<string>(message =>
            {
                using var hmac = new HMACSHA256(key);
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
            });

        _hasher = new HmacOtpHasher(keyService.Object, _passwordHasher.Object);
    }

    [Fact]
    public void Hash_ThenVerify_AcceptsTheCode()
    {
        var scope = Guid.NewGuid().ToString();

        _hasher.Verify(scope, "123456", _hasher.Hash(scope, "123456")).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsAWrongCode()
    {
        var scope = Guid.NewGuid().ToString();

        _hasher.Verify(scope, "999999", _hasher.Hash(scope, "123456")).Should().BeFalse();
    }

    /// <summary>
    /// The stored value is bound to whoever the code belongs to. Without this,
    /// every row holding the same six digits would hold the same bytes: visible
    /// as a match to anyone reading the table, and reusable as a precomputed set
    /// from one row to the next if the key ever leaked.
    /// </summary>
    [Fact]
    public void TheSameCode_HashesDifferentlyForDifferentSubjects()
    {
        var first = _hasher.Hash(Guid.NewGuid().ToString(), "123456");
        var second = _hasher.Hash(Guid.NewGuid().ToString(), "123456");

        first.Should().NotBe(second);
    }

    [Fact]
    public void ACodeMintedForOneSubject_DoesNotVerifyForAnother()
    {
        var stored = _hasher.Hash(Guid.NewGuid().ToString(), "123456");

        _hasher.Verify(Guid.NewGuid().ToString(), "123456", stored).Should().BeFalse();
    }

    /// <summary>
    /// The deployment courtesy: codes minted by the password hasher moments
    /// before this shipped must still be redeemable. Nothing migrates them —
    /// they expire in minutes and the branch stops being reached on its own.
    /// </summary>
    [Fact]
    public void ACodeStoredByThePasswordHasher_IsStillAccepted()
    {
        _passwordHasher
            .Setup(h => h.VerifyPassword("123456", LegacyArgon2Hash))
            .Returns(true);

        _hasher.Verify("any-scope", "123456", LegacyArgon2Hash).Should().BeTrue();

        _passwordHasher.Verify(h => h.VerifyPassword("123456", LegacyArgon2Hash), Times.Once);
    }

    [Fact]
    public void AKeyedHash_IsNeverHandedToThePasswordHasher()
    {
        var scope = Guid.NewGuid().ToString();

        _hasher.Verify(scope, "123456", _hasher.Hash(scope, "123456"));

        _passwordHasher.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "the two forms are told apart by a prefix base64 can never produce, not by guessing");
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData("123456", "")]
    public void Verify_RefusesEmptyInput(string code, string storedHash)
    {
        _hasher.Verify("scope", code, storedHash).Should().BeFalse();
    }

    /// <summary>
    /// Domain separation. The key is shared with refresh-token and invitation
    /// hashing, so an OTP digest must not equal the digest that key would produce
    /// for the bare code — otherwise a value from one use could be replayed into
    /// another.
    /// </summary>
    [Fact]
    public void TheDigest_IsNotThePlainKeyedHashOfTheCode()
    {
        var key = Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-bytes-long!");
        using var hmac = new HMACSHA256(key);
        var bare = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("123456")));

        _hasher.Hash("scope", "123456").Should().NotBe(bare);
    }

    /// <summary>
    /// Every place that mints or checks a confirmation code goes through the OTP
    /// hasher.
    /// </summary>
    /// <remarks>
    /// The point of the change is throughput and a key outside the database, and
    /// both are lost the moment one path keeps paying the password cost — quietly,
    /// because a missed path still works. Seven files handle codes, and this
    /// enumerates them: a new one is a decision about which hasher a code
    /// deserves, and it should cost an edit here rather than pass unnoticed.
    /// </remarks>
    [Theory]
    [InlineData("Features/Authentication/SendEmailVerification/SendEmailVerificationCommandHandler.cs")]
    [InlineData("Features/Authentication/ResendEmailVerification/ResendEmailVerificationCommandHandler.cs")]
    [InlineData("Features/Authentication/VerifyEmail/VerifyEmailCommandHandler.cs")]
    [InlineData("Features/AccountDeletion/Common/DeletionOtpService.cs")]
    [InlineData("Features/Secrets/Common/SecretOperationChallengeService.cs")]
    [InlineData("Features/Organizations/InitiateOwnershipTransfer/InitiateOwnershipTransferCommandHandler.cs")]
    [InlineData("Features/Organizations/TransferOwnership/TransferOwnershipCommandHandler.cs")]
    public void EveryCodePath_UsesTheOtpHasher(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Application", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        source.Should().Contain("IOtpHasher",
            "a confirmation code hashed at password cost pays fifteen hours of protection "
            + "for a secret that is dead in five minutes");
        source.Should().NotContain("IPasswordHasher",
            "the password hasher has no remaining business on a code path, and leaving the "
            + "dependency behind invites the next edit to use it");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find Auth.sln above their output folder");
        return directory!.FullName;
    }
}
