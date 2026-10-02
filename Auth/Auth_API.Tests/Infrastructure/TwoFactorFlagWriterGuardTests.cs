using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// One source of truth for "two-factor is on". The sign-in gate reads
/// <c>Users.IsTwoFactorEnabled</c>; verification reads <c>TwoFactorAuth.IsEnabled</c>.
/// The flag used to be written by the whole-row user write from a copy read
/// earlier — an avatar adopted at sign-in, a profile edit — so a stale copy could
/// switch two-factor off behind the user's back. Now only the store's transactions
/// that change the factor row write it.
/// </summary>
public class TwoFactorFlagWriterGuardTests
{
    // An UPDATE of Users whose SET list (up to its WHERE) assigns the flag. C#
    // parameter objects ("IsTwoFactorEnabled = user.TwoFactorEnabled") come after
    // the statement's WHERE, so they are not mistaken for a write.
    private static readonly Regex FlagWrite = new(
        @"UPDATE\s+(?:\w+\s+SET|(?:\[?dbo\]?\.)?\[?Users\]?\s+SET)(?:(?!\bWHERE\b|;)[\s\S])*?\[?IsTwoFactorEnabled\]?\s*=",
        RegexOptions.IgnoreCase);

    [Fact]
    public async Task UpdateAsync_OmitsTwoFactorFlag()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var repository = new UserRepository(
            db,
            Snapshot(new PasswordSettings()),
            Mock.Of<IIdentifierHasher>(),
            Snapshot(new AccountDeletionSettings()),
            Mock.Of<IPerUserCryptoService>(),
            Mock.Of<IOtpHasher>());
        var user = TestHelpers.CreateUser(twoFactorEnabled: false);

        await repository.UpdateAsync(user, CancellationToken.None);

        var update = db.Commands.Single(command => command.CommandText.Contains("UPDATE [dbo].[Users] SET", StringComparison.Ordinal));
        update.CommandText.Should().Contain("[Email] = @Email", "the write under test must have been recorded");
        update.CommandText.Should().NotContain("IsTwoFactorEnabled",
            "a whole-row write built from a stale copy must never switch two-factor off");
        update.Parameters.Keys.Should().NotContain("IsTwoFactorEnabled");
    }

    [Fact]
    public void TheFlag_IsWrittenOnlyByTheStoreAndTheReconcileScript()
    {
        var root = ApiSourceScan.SolutionDirectory();

        var codeWriters = ApiSourceScan.ProductionSources()
            .Where(source => FlagWrite.IsMatch(source.Source))
            .Select(source => Path.GetFileName(source.File))
            .ToList();

        codeWriters.Should().BeEquivalentTo(["TwoFactorStateStore.cs"],
            "the flag changes only with the factor row, inside the store's transactions — and the scan must find that one writer");

        var sqlWriters = Directory.EnumerateFiles(Path.Combine(root, "Auth_DB"), "*.sql", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => FlagWrite.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        sqlWriters.Should().BeEquivalentTo(["2026-10-02_TwoFactorFlagReconcile.sql"],
            "no stored procedure or deployed script may write the flag; the manual reconcile is the one exception");
    }

    private static IOptionsSnapshot<T> Snapshot<T>(T value) where T : class
    {
        var snapshot = new Mock<IOptionsSnapshot<T>>();
        snapshot.Setup(s => s.Value).Returns(value);
        return snapshot.Object;
    }
}
