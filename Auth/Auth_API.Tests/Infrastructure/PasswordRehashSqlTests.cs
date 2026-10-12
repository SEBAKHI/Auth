using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// X12 PR B (review): a sign-in that re-hashes the password under new parameters
/// writes the hash alone. <c>LastPasswordChangeUtc</c> is the stamp the
/// administrator's reset of a hard-locked second factor trusts as the owner's own
/// password change (<c>TwoFactorSnapshot.EnsureAdministratorMayReset</c>); were the
/// re-hash to stamp it, whoever knows the password could open that gate by signing
/// in once after any Argon2 or pepper change. Nor does it clear an administrator's
/// <c>MustChangePassword</c>.
/// </summary>
/// <remarks>
/// Deliberate breaks: stamp <c>[LastPasswordChangeUtc]</c> in the re-hash; drop the
/// verified-hash condition (a reset that lands during the sign-in's hashing would be
/// undone by the old password: the gate open, and the old password working again).
/// </remarks>
public class PasswordRehashSqlTests
{
    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    [Fact]
    public async Task Rehash_WritesTheHashAlone()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var repository = new UserRepository(
            db,
            TestHelpers.CreateOptions(new PasswordSettings()),
            Mock.Of<IIdentifierHasher>(),
            TestHelpers.CreateOptions(new AccountDeletionSettings()),
            Mock.Of<IPerUserCryptoService>(),
            Mock.Of<IOtpHasher>());

        await repository.RehashPasswordAsync(userId, "verified-hash", "new-hash", CancellationToken.None);

        var command = db.Commands.Should().ContainSingle().Subject;
        Sql(command).Should().Be(
            "UPDATE dbo.Users SET PasswordHash = @PasswordHash WHERE Id = @UserId AND PasswordHash = @VerifiedHash");
        command.Parameters["UserId"].Should().Be(userId);
        command.Parameters["PasswordHash"].Should().Be("new-hash");
        command.Parameters["VerifiedHash"].Should().Be("verified-hash");
    }

    [Fact]
    public void Rehash_WritesOnlyOverTheHashItVerified()
    {
        Sql(new RecordedCommand(UserRepository.RehashPasswordSql, new Dictionary<string, object?>()))
            .Should().EndWith("WHERE Id = @UserId AND PasswordHash = @VerifiedHash",
                "a reset that landed while the sign-in hashed must stand");
    }

    [Fact]
    public void Rehash_NeverStampsAPasswordChange()
    {
        UserRepository.RehashPasswordSql.Should().NotContain("LastPasswordChangeUtc")
            .And.NotContain("MustChangePassword");
    }
}
