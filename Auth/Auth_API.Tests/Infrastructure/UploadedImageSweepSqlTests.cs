using System.Text.RegularExpressions;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards OI-85's SQL: the uploads sweep (per batch: attach what something
/// references, then delete what nothing references) and the logo claim.
/// <para>
/// A dropped branch in the reference predicate deletes images that are in use,
/// and a dropped uploader check lets anyone claim anyone's upload, both with
/// every other test green. The test project has no database, so two kinds of
/// evidence stand in for one: the repository runs on a recording connection
/// (every statement, whether it carried the transaction, and how the transaction
/// ended), and the SQL text it sent is checked for what a recording cannot
/// evaluate: which columns count as a reference.
/// </para>
/// </summary>
public class UploadedImageSweepSqlTests
{
    private const string BatchEnd = "k-020.webp";
    private const string ReclaimedKey = "k-007.webp";
    private const string ExistsMarker = "EXISTS (SELECT 1 FROM [dbo].[";

    private static readonly DateTime OlderThan = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Single-value columns: the key itself, or a composed URL ending in "/" + key.</summary>
    public static TheoryData<string, string> SingleValueColumns => new()
    {
        { "Users", "ProfileImageUrl" },
        { "Applications", "LogoUrl" },
        { "Organizations", "LogoUrl" },
        { "PlatformSettings", "LogoUrl" },
        { "PlatformSettings", "LogoUrlDark" },
        { "PlatformSettings", "FaviconUrl" },
    };

    /// <summary>HTML columns: the key anywhere in the text.</summary>
    public static TheoryData<string, string> HtmlColumns => new()
    {
        { "NotificationTemplateTranslations", "BodyHtml" },
        { "NotificationTemplateTranslations", "BodyText" },
        { "NotificationLayouts", "DraftContent" },
        { "NotificationLayouts", "PublishedContent" },
    };

    /// <summary>
    /// A connection on which the batch-end lookup answers <paramref name="batchEnd"/>
    /// (null: nothing left), the adopt attaches 3 rows, and the reclaim returns one key.
    /// </summary>
    private static RecordingDbConnectionFactory Connection(
        string? batchEnd = BatchEnd, Func<RecordedCommand, Exception?>? throwOn = null) =>
        new(
            affectedRows: 0,
            rowFor: command => command.CommandText.Contains("DELETE u") ? new { StorageKey = ReclaimedKey } : null,
            throwOn: throwOn,
            affectedFor: command => command.CommandText.Contains("UPDATE u") ? 3 : 0,
            scalarFor: command => command.CommandText.Contains("SELECT MAX(b.[StorageKey])")
                ? batchEnd
                : throw new InvalidOperationException($"Unexpected scalar: {command.CommandText}"));

    private static async Task<(RecordingDbConnectionFactory Db, UploadSweepBatch? Batch)> SweepOneBatch(
        string after = "", string? batchEnd = BatchEnd)
    {
        var db = Connection(batchEnd);
        var batch = await new UploadedImageRepository(db).SweepBatchAsync(after, OlderThan, CancellationToken.None);
        return (db, batch);
    }

    private static RecordedCommand Adopt(RecordingDbConnectionFactory db) =>
        db.Commands.Single(command => command.CommandText.Contains("UPDATE u"));

    private static RecordedCommand Reclaim(RecordingDbConnectionFactory db) =>
        db.Commands.Single(command => command.CommandText.Contains("DELETE u"));

    /// <summary>Both statements that carry the reference predicate, as sent.</summary>
    private static async Task<string[]> PredicateStatements()
    {
        var (db, _) = await SweepOneBatch();
        return [Adopt(db).CommandText, Reclaim(db).CommandText];
    }

    /// <summary>The EXISTS block of one referencing table in a statement.</summary>
    private static string ExistsBlock(string sql, string table)
    {
        var blocks = sql.Split(ExistsMarker).Skip(1)
            .Where(block => block.StartsWith($"{table}] r", StringComparison.Ordinal))
            .ToList();
        blocks.Should().ContainSingle($"[dbo].[{table}] is checked exactly once in the reference predicate");
        return blocks[0];
    }

    [Theory]
    [MemberData(nameof(SingleValueColumns))]
    public async Task ReferencePredicate_SingleValueColumn_MatchesTheKeyOrAComposedUrlEndingInIt(string table, string column)
    {
        foreach (var sql in await PredicateStatements())
        {
            var block = ExistsBlock(sql, table);
            block.Should().Contain($"r.[{column}] = u.[StorageKey]");
            block.Should().Contain($"r.[{column}] LIKE N'%/' + u.[StorageKey]",
                "rows written before key normalization hold the composed absolute URL");
        }
    }

    [Theory]
    [MemberData(nameof(HtmlColumns))]
    public async Task ReferencePredicate_HtmlColumn_MatchesTheKeyAnywhereInTheText(string table, string column)
    {
        foreach (var sql in await PredicateStatements())
        {
            ExistsBlock(sql, table).Should().Contain($"CHARINDEX(u.[StorageKey], r.[{column}]) > 0");
        }
    }

    [Fact]
    public async Task ReferencePredicate_ChecksExactlyTheSixTablesAndCountsSoftDeletedRows()
    {
        foreach (var sql in await PredicateStatements())
        {
            Regex.Matches(sql, Regex.Escape(ExistsMarker)).Should().HaveCount(6);
            sql.Should().NotContain("IsDeleted", "a restore must find its image");
        }
    }

    [Fact]
    public async Task Adopt_AttachesReferencedRowsOfTheBatchWhateverTheirAge()
    {
        var (db, _) = await SweepOneBatch();
        var sql = Adopt(db).CommandText;

        sql.Should().Contain("SET [AttachedAt] = GETUTCDATE()");
        sql.Should().Contain("u.[AttachedAt] IS NULL");
        sql.Should().Contain("u.[StorageKey] > @After");
        sql.Should().Contain("u.[StorageKey] <= @Last");
        Regex.IsMatch(sql, @"AND \(\s*EXISTS").Should().BeTrue("the adopt keeps what is referenced");
        sql.Should().NotContain("UploadedAt", "an upload referenced today is kept from today");
    }

    [Fact]
    public async Task Reclaim_IsOneDeleteOfOldUnreferencedRowsOfTheBatch()
    {
        var (db, _) = await SweepOneBatch();
        var sql = Reclaim(db).CommandText;

        Regex.Matches(sql, @"\bDELETE\b").Should().ContainSingle("listing and deleting are one statement");
        sql.Should().Contain("OUTPUT DELETED.[StorageKey]");
        sql.Should().Contain("u.[AttachedAt] IS NULL");
        sql.Should().Contain("u.[UploadedAt] < @OlderThan");
        sql.Should().Contain("u.[StorageKey] > @After");
        sql.Should().Contain("u.[StorageKey] <= @Last");
        Regex.IsMatch(sql, @"AND NOT \(\s*EXISTS").Should().BeTrue(
            "the reference check is repeated inside the delete, so a reference saved after the adopt still counts");
    }

    [Fact]
    public async Task SweepBatch_LooksUpTheBatchOutsideAndRunsBothStatementsInOneCommittedTransaction()
    {
        var (db, batch) = await SweepOneBatch();

        db.Commands.Should().HaveCount(3);
        db.Commands[0].CommandText.Should().Contain("SELECT MAX(b.[StorageKey])");
        db.Commands[0].InTransaction.Should().BeFalse();
        db.Commands[0].Parameters["After"].Should().Be("");
        db.Commands[0].Parameters["BatchSize"].Should().Be(20);

        db.Commands[1].Should().Be(Adopt(db));
        db.Commands[2].Should().Be(Reclaim(db));
        db.Commands[1].InTransaction.Should().BeTrue();
        db.Commands[2].InTransaction.Should().BeTrue("the adopt and the reclaim must see the same rows");
        db.Transactions.Should().ContainSingle();
        db.Transactions[0].Committed.Should().BeTrue();
        db.Transactions[0].RolledBack.Should().BeFalse();

        foreach (var statement in new[] { Adopt(db), Reclaim(db) })
        {
            statement.Parameters["After"].Should().Be("");
            statement.Parameters["Last"].Should().Be(BatchEnd);
        }

        Reclaim(db).Parameters["OlderThan"].Should().Be(OlderThan);

        batch.Should().NotBeNull();
        batch!.Next.Should().Be(BatchEnd);
        batch.Adopted.Should().Be(3);
        batch.Reclaimed.Should().Equal(ReclaimedKey);
    }

    [Fact]
    public async Task SweepBatch_StartsAfterTheKeyItIsGiven()
    {
        var (db, batch) = await SweepOneBatch(after: BatchEnd, batchEnd: "k-040.webp");

        db.Commands[0].Parameters["After"].Should().Be(BatchEnd);
        Adopt(db).Parameters["After"].Should().Be(BatchEnd);
        Adopt(db).Parameters["Last"].Should().Be("k-040.webp");
        Reclaim(db).Parameters["After"].Should().Be(BatchEnd);
        Reclaim(db).Parameters["Last"].Should().Be("k-040.webp");
        batch!.Next.Should().Be("k-040.webp");
    }

    [Fact]
    public async Task SweepBatch_NothingLeft_ReturnsNullAndOpensNoTransaction()
    {
        var (db, batch) = await SweepOneBatch(batchEnd: null);

        batch.Should().BeNull();
        db.Commands.Should().ContainSingle();
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task SweepBatch_ReclaimFailing_CommitsNothing()
    {
        var db = Connection(throwOn: command =>
            command.CommandText.Contains("DELETE u") ? new TimeoutException("reclaim timed out") : null);

        var act = () => new UploadedImageRepository(db).SweepBatchAsync("", OlderThan, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        db.Transactions.Should().ContainSingle();
        db.Transactions[0].Committed.Should().BeFalse("the adopt is undone with the failed reclaim");
    }

    [Fact]
    public async Task TryClaim_UpdatesOnlyARowTheActorUploaded_KeepingTheFirstAttachTime()
    {
        var actor = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var claimed = await new UploadedImageRepository(db).TryClaimAsync("k.webp", actor, CancellationToken.None);

        claimed.Should().BeTrue();
        var claim = db.Commands.Should().ContainSingle().Subject;
        claim.InTransaction.Should().BeFalse();
        claim.CommandText.Should().Contain("UPDATE [dbo].[UploadedImages]");
        claim.CommandText.Should().Contain("[StorageKey] = @StorageKey");
        claim.CommandText.Should().Contain("[UploadedBy] = @UserId", "only the uploader may claim a key");
        claim.CommandText.Should().Contain("COALESCE([AttachedAt], GETUTCDATE())");
        claim.Parameters["StorageKey"].Should().Be("k.webp");
        claim.Parameters["UserId"].Should().Be(actor);
    }

    [Fact]
    public async Task TryClaim_NoRowAffected_IsFalse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var claimed = await new UploadedImageRepository(db).TryClaimAsync("k.webp", Guid.NewGuid(), CancellationToken.None);

        claimed.Should().BeFalse();
    }
}
