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
    // An UPDATE whose SET list (up to its own WHERE) assigns the flag, whatever its
    // head: a table hint (WITH (ROWLOCK)), TOP (n), a MERGE's bare "UPDATE SET", or
    // a parenthesised subquery — with a WHERE of its own — earlier in the list. A
    // parenthesised group is stepped over whole, so only the statement's own WHERE
    // ends the SET list. C# parameter objects ("IsTwoFactorEnabled =
    // user.TwoFactorEnabled") come after the statement's WHERE, so they are not
    // mistaken for a write.
    private static readonly Regex FlagWrite = new(
        @"UPDATE\s+(?:TOP\s*\([^)]*\)\s+)?(?:(?:\[?\w+\]?\.)?\[?\w+\]?(?:\s+WITH\s*\([^)]*\))?\s+)?SET\b"
        + @"(?:(?!\bWHERE\b)[^();]|\((?>[^()]+|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!))\))*?"
        + @"\[?IsTwoFactorEnabled\]?\s*=",
        RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @Id", true)]
    [InlineData("UPDATE u SET u.[IsTwoFactorEnabled] = 0 FROM [dbo].[Users] u WHERE u.[Id] = @Id", true)]
    [InlineData("UPDATE dbo.Users SET IsTwoFactorEnabled = 1", true)]
    // OI-45 (1), C-F1: the forms the guard used to miss, two of them already used
    // elsewhere in this repository.
    [InlineData("UPDATE [dbo].[Users] WITH (ROWLOCK) SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @Id", true)]
    [InlineData("UPDATE TOP (1) [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @Id", true)]
    [InlineData("MERGE [dbo].[Users] AS t USING (SELECT @Id AS Id) AS s ON t.[Id] = s.Id WHEN MATCHED THEN UPDATE SET t.[IsTwoFactorEnabled] = 0;", true)]
    [InlineData("UPDATE [dbo].[Users] SET [ModifiedAt] = (SELECT MAX([CreatedAt]) FROM [dbo].[AuditLogs] WHERE [UserId] = @Id), [IsTwoFactorEnabled] = 0 WHERE [Id] = @Id", true)]
    // Reads and parameter objects are not writes.
    [InlineData("SELECT [IsTwoFactorEnabled] FROM [dbo].[Users] WHERE [IsTwoFactorEnabled] = 1", false)]
    [InlineData("UPDATE [dbo].[Users] SET [Email] = @Email WHERE [Id] = @Id\", new { IsTwoFactorEnabled = user.TwoFactorEnabled }", false)]
    [InlineData("INSERT INTO [dbo].[Users] ([IsTwoFactorEnabled]) VALUES (@IsTwoFactorEnabled)", false)]
    [InlineData("UPDATE [dbo].[Sessions] SET [EndedAt] = SYSUTCDATETIME() WHERE [UserId] IN (SELECT [Id] FROM [dbo].[Users] WHERE [IsTwoFactorEnabled] = 0)", false)]
    public void FlagWrite_SeesEveryWayToAssignTheFlag(string sql, bool isWrite)
    {
        FlagWrite.IsMatch(sql).Should().Be(isWrite, sql);
    }

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
