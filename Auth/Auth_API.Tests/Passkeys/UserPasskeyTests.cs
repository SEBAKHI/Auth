using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK5, PK17, T-A8, T-A9): a passkey is created only from a verified registration with
/// user verification, a credential id of 1 to 1023 bytes and a key; its name is at most 64 code
/// points (counted as runes, never UTF-16 units), blank naming it "Passkey"; and a revoked one
/// keeps the first reason.
/// </summary>
/// <remarks>Deliberate break, verified red: the name counted in <c>string.Length</c> (64 emoji refused).</remarks>
public class UserPasskeyTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 14, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_FromAVerifiedRegistration_IsLive_AndUserVerified()
    {
        var passkey = Create().Value;

        passkey.IsLive.Should().BeTrue();
        passkey.IsUserVerified.Should().BeTrue();
        passkey.Name.Should().Be("Laptop");
        passkey.CreatedAt.Should().Be(Now);
        passkey.RpId.Should().Be("example.com");
        passkey.Id.Should().NotBeEmpty();
    }

    public static TheoryData<string, bool, byte[], byte[], string, long> Refused => new()
    {
        { "no user verification", false, [1], [9], "example.com", 0 },
        { "an empty credential id", true, [], [9], "example.com", 0 },
        { "a credential id of 1024 bytes", true, new byte[1024], [9], "example.com", 0 },
        { "no public key", true, [1], [], "example.com", 0 },
        { "no RP ID", true, [1], [9], " ", 0 },
        { "a negative counter", true, [1], [9], "example.com", -1 },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Create_RefusesWhatNoVerifiedRegistrationGives(string because, bool userVerified, byte[] credentialId, byte[] publicKey, string rpId, long signCount)
    {
        var created = UserPasskey.Create(Guid.NewGuid(), credentialId, publicKey, signCount, rpId, userVerified, false, false, "Laptop", Now);

        created.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, because);
    }

    [Fact]
    public void Create_ACredentialIdOf1023Bytes_IsAccepted()
    {
        UserPasskey.Create(Guid.NewGuid(), new byte[1023], [9], 0, "example.com", true, false, false, null, Now).IsError.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "Passkey")]
    [InlineData("", "Passkey")]
    [InlineData("   ", "Passkey")]
    [InlineData("  Phone  ", "Phone")]
    public void TheName_IsTrimmed_AndBlankIsPasskey(string? name, string expected)
    {
        UserPasskey.NormalizeName(name).Value.Should().Be(expected);
    }

    [Fact]
    public void TheName_IsCountedInCodePoints()
    {
        var sixtyFourEmoji = string.Concat(Enumerable.Repeat("😀", 64));

        sixtyFourEmoji.Length.Should().Be(128, "each emoji is two UTF-16 units");
        UserPasskey.NormalizeName(sixtyFourEmoji).Value.Should().Be(sixtyFourEmoji);
        UserPasskey.NormalizeName(sixtyFourEmoji + "😀").FirstError.Code.Should().Be(PasskeyErrors.NameTooLong.Code);
        UserPasskey.NormalizeName(new string('a', 64)).IsError.Should().BeFalse();
        UserPasskey.NormalizeName(new string('a', 65)).FirstError.Code.Should().Be(PasskeyErrors.NameTooLong.Code);
    }

    [Fact]
    public void Revoke_StopsIt_AndKeepsTheFirstReason()
    {
        var passkey = Create().Value;
        var by = Guid.NewGuid();

        passkey.Revoke(PasskeyRemovalReasons.PasswordReset, by, Now.AddHours(1));
        passkey.Revoke(PasskeyRemovalReasons.User, Guid.NewGuid(), Now.AddHours(2));

        passkey.IsLive.Should().BeFalse();
        passkey.RevokedReason.Should().Be(PasskeyRemovalReasons.PasswordReset);
        passkey.RevokedBy.Should().Be(by);
        passkey.RevokedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Rename_RefusesTooLong_AndKeepsTheOldName()
    {
        var passkey = Create().Value;

        passkey.Rename(new string('a', 65)).FirstError.Code.Should().Be(PasskeyErrors.NameTooLong.Code);
        passkey.Name.Should().Be("Laptop");
        passkey.Rename(" Work ").IsError.Should().BeFalse();
        passkey.Name.Should().Be("Work");
    }

    [Fact]
    public void TheReasons_FitTheColumn()
    {
        // UserPasskeys.RevokedReason is NVARCHAR(32).
        string[] reasons =
        [
            PasskeyRemovalReasons.User, PasskeyRemovalReasons.PeerReset, PasskeyRemovalReasons.PasswordReset,
            PasskeyRemovalReasons.PasswordChange, PasskeyRemovalReasons.AdminLock, PasskeyRemovalReasons.RpIdMove,
            PasskeyRemovalReasons.BreakGlass,
        ];

        reasons.Should().OnlyHaveUniqueItems().And.OnlyContain(reason => reason.Length > 0 && reason.Length <= 32);
        PasskeyRemovalReasons.RecentRegistrationWindow.Should().Be(TimeSpan.FromHours(72));
    }

    /// <summary>
    /// REVIEW-FIXES 150 F18 (h): the name is shown back in the owner's list, the audit and notices
    /// that may say "if this was not you", so nothing in it may hide or reorder what it shows:
    /// control characters and the bidi embedding, override and isolate controls are dropped, at
    /// creation and on rename. ZWNJ and ZWJ stay: Persian is written with them, and emoji
    /// sequences need ZWJ.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the filter removed.</remarks>
    [Theory]
    [InlineData("Lap\u0000top\u0007", "Laptop")]
    [InlineData("\u202EpotpaL", "potpaL")]
    [InlineData("Work \u202Alaptop\u202C", "Work laptop")]
    [InlineData("\u2066Phone\u2069 \u2067x\u2068", "Phone x")]
    [InlineData("Key\r\n\tTwo\u0085\u009B", "KeyTwo")]
    [InlineData("\u0007\u202E", UserPasskey.DefaultName)]
    [InlineData("کلید\u200Cعبور", "کلید\u200Cعبور")]
    [InlineData("\U0001F469\u200D\U0001F4BB laptop", "\U0001F469\u200D\U0001F4BB laptop")]
    public void TheName_DropsControlAndBidiCharacters_KeepsZwnjAndZwj(string name, string expected)
    {
        var created = UserPasskey.Create(Guid.NewGuid(), [1, 2, 3], [9, 9], 0, "example.com", true, true, false, name, Now);
        created.Value.Name.Should().Be(expected, "at creation");

        var passkey = Create().Value;
        passkey.Rename(name).IsError.Should().BeFalse();
        passkey.Name.Should().Be(expected, "on rename");
    }

    private static ErrorOr.ErrorOr<UserPasskey> Create() =>
        UserPasskey.Create(Guid.NewGuid(), [1, 2, 3], [9, 9], 0, "example.com", true, true, false, "Laptop", Now);
}
