using System.Data;
using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.LoginDesigns;

/// <summary>
/// The sign-in page designs through Dapper and in the schema: what the repository sends and
/// reads back, and the table the database holds them in. A recording connection stands in for
/// the database; the activation swap and its race were also run, as written, against a
/// throwaway LocalDB database (see the PR).
/// </summary>
/// <remarks>
/// Deliberate breaks: send the revision as Dapper's default <c>datetime</c> (the datetime2 case
/// turns red); drop <c>AND [IsActive] = 0</c> from the delete; drop the filtered index's
/// <c>WHERE [IsActive] = 1</c>; let the activation commit after a unique violation.
/// </remarks>
public class LoginDesignPersistenceTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static string Flat(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    private static LoginDesign Design(Guid? applicationId = null) =>
        LoginDesign.Create(applicationId, "Design", "<p>x</p>", null, true, Admin).Value;

    [Fact]
    public async Task Create_WritesEveryColumn_WithTimestampsAsDatetime2()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var design = Design(Guid.NewGuid());

        await new LoginDesignRepository(db).CreateAsync(design, CancellationToken.None);

        var command = db.LastCommand!;
        command.Parameters["Id"].Should().Be(design.Id);
        command.Parameters["ApplicationId"].Should().Be(design.ApplicationId);
        command.Parameters["IsActive"].Should().Be(false);
        command.Parameters["ContentHash"].Should().Be(design.ContentHash);
        command.Parameters["CreatedAt"].Should().Be(design.CreatedAt);
        command.ParameterTypes["CreatedAt"].Should().Be(DbType.DateTime2,
            "Dapper's default datetime rounds to 3 ms, and the revision an editor saves against would no longer match");
        command.ParameterTypes["ModifiedAt"].Should().Be(DbType.DateTime2);
    }

    [Fact]
    public async Task Update_WritesOnlyWhileTheRevisionMatches_AndNeverTheActiveFlag()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var design = Design();
        var revision = design.CreatedAt;
        design.Update("New", "<p>y</p>", null, false, Admin);

        var saved = await new LoginDesignRepository(db).TryUpdateAsync(design, revision, CancellationToken.None);

        saved.Should().BeTrue();
        var sql = Flat(db.LastCommand!.CommandText);
        sql.Should().Contain("WHERE [Id] = @Id AND COALESCE([ModifiedAt], [CreatedAt]) = @ExpectedRevision");
        sql.Should().NotContain("[IsActive]", "activation has its own transaction");
        db.LastCommand.Parameters["ExpectedRevision"].Should().Be(revision);
        db.LastCommand.ParameterTypes["ExpectedRevision"].Should().Be(DbType.DateTime2);
        db.LastCommand.ParameterTypes["ModifiedAt"].Should().Be(DbType.DateTime2);
    }

    [Fact]
    public async Task Update_OfAChangedRow_AnswersFalse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await new LoginDesignRepository(db).TryUpdateAsync(Design(), DateTime.UtcNow, CancellationToken.None))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Activate_SwapsInOneTransaction_ScopedToTheDesignsScope(bool platform)
    {
        var previous = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT [Id]") ? new { Id = previous } : null);
        var design = Design(platform ? null : Guid.NewGuid());

        var activation = await new LoginDesignRepository(db).ActivateAsync(design, CancellationToken.None);

        activation.Should().Be(new LoginDesignActivation(true, previous));
        db.Transactions.Should().ContainSingle();
        db.LastTransaction!.Committed.Should().BeTrue();
        db.Commands.Should().HaveCount(3);
        var scope = platform ? "[ApplicationId] IS NULL" : "[ApplicationId] = @ApplicationId";
        Flat(db.Commands[1].CommandText).Should().Be(
            $"UPDATE [dbo].[LoginDesigns] SET [IsActive] = 0 WHERE {scope} AND [IsActive] = 1 AND [Id] <> @Id");
        Flat(db.Commands[2].CommandText).Should().Be(
            $"UPDATE [dbo].[LoginDesigns] SET [IsActive] = 1 WHERE [Id] = @Id AND {scope}");
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task Activate_LosingToAConcurrentActivation_RollsBack_AndAnswersConflict(int number)
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("SET [IsActive] = 1") ? SqlExceptions.WithNumber(number) : null);

        var activation = await new LoginDesignRepository(db).ActivateAsync(Design(), CancellationToken.None);

        activation.Should().Be(LoginDesignActivation.Conflict);
        db.LastTransaction!.RolledBack.Should().BeTrue();
        db.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task Activate_OfADeletedDesign_RollsBack()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            affectedFor: command => command.CommandText.Contains("SET [IsActive] = 1") ? 0 : 1);

        var activation = await new LoginDesignRepository(db).ActivateAsync(Design(), CancellationToken.None);

        activation.Activated.Should().BeFalse();
        db.LastTransaction!.RolledBack.Should().BeTrue("the previous design must not be left switched off");
    }

    [Fact]
    public async Task Delete_RemovesOnlyAnInactiveRow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new LoginDesignRepository(db).DeleteInactiveAsync(Guid.NewGuid(), CancellationToken.None);

        Flat(db.LastCommand!.CommandText).Should().Be("DELETE FROM [dbo].[LoginDesigns] WHERE [Id] = @Id AND [IsActive] = 0");
    }

    [Fact]
    public async Task Reads_HydrateEveryColumn()
    {
        var id = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var created = new DateTime(2026, 10, 29, 10, 0, 0, 123, DateTimeKind.Utc).AddTicks(4567);
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = id,
                ApplicationId = (Guid?)applicationId,
                Name = "Spring",
                Content = "<p>c</p>",
                StringsJson = """{"en":{"a":"b"}}""",
                ShowPlatformLogo = false,
                IsActive = true,
                ContentHash = new string('a', 64),
                CreatedAt = created,
                CreatedBy = Admin,
                ModifiedAt = (DateTime?)null,
                ModifiedBy = (Guid?)null
            });

        var design = await new LoginDesignRepository(db).GetActiveAsync(applicationId, CancellationToken.None);

        Flat(db.LastCommand!.CommandText).Should().EndWith("WHERE [ApplicationId] = @ApplicationId AND [IsActive] = 1");
        design!.Id.Should().Be(id);
        design.Name.Should().Be("Spring");
        design.StringsJson.Should().Be("""{"en":{"a":"b"}}""");
        design.ShowPlatformLogo.Should().BeFalse();
        design.IsActive.Should().BeTrue();
        design.IsRevision(created).Should().BeTrue();
    }

    [Fact]
    public async Task ThePlatformsScope_IsTheRowsWithoutAnApplication()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new LoginDesignRepository(db).GetByScopeAsync(null, CancellationToken.None);

        Flat(db.LastCommand!.CommandText).Should().Contain("WHERE [ApplicationId] IS NULL ORDER BY [IsActive] DESC, [Name]");
    }

    // --- the schema ---

    private static string TableSource() =>
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "System", "LoginDesigns.sql"));

    [Fact]
    public void AtMostOneActiveDesignPerScope_IsHeldByAFilteredUniqueIndex()
    {
        Flat(TableSource()).Should().Contain(
            "CREATE UNIQUE NONCLUSTERED INDEX [UX_LoginDesigns_ActivePerScope] ON [dbo].[LoginDesigns] ([ApplicationId]) WHERE [IsActive] = 1;",
            "NULLs are equal in a unique index, so the platform's designs share one slot too");
    }

    [Fact]
    public void EveryDefault_IsNamed_AndANewDesignStartsInactive_ShowingThePlatformsMark()
    {
        var source = TableSource();
        Regex.Matches(source, @"\bDEFAULT\b").Count.Should().Be(
            Regex.Matches(source, @"CONSTRAINT \[DF_LoginDesigns_\w+\] DEFAULT").Count);
        source.Should().Contain("[IsActive] BIT NOT NULL CONSTRAINT [DF_LoginDesigns_IsActive] DEFAULT 0");
        source.Should().Contain("[ShowPlatformLogo] BIT NOT NULL CONSTRAINT [DF_LoginDesigns_ShowPlatformLogo] DEFAULT 1");
        source.Should().Contain("CONSTRAINT [CK_LoginDesigns_StringsJson] CHECK (ISJSON([StringsJson]) = 1)");
    }

    [Fact]
    public void TheTable_IsInTheDatabaseProject_AndReadinessNamesIt()
    {
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "Auth_DB.sqlproj"))
            .Should().Contain(@"<Build Include=""dbo\Tables\System\LoginDesigns.sql"" />");
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(expectation =>
            expectation.Name == "LoginDesigns table"
            && expectation.Sql.Contains("OBJECT_ID('dbo.LoginDesigns', 'U') IS NOT NULL"));
    }
}
