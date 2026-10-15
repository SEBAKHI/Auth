using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-151: the post-logout allowlist through Dapper, as
/// <see cref="GrantedScopePersistenceTests"/> pins the scope columns. The test
/// project has no database, so a recording connection stands in: Dapper builds
/// the real commands and parameters, and maps a row shaped like the table's.
/// The table script and these statements also ran on a throwaway LocalDB
/// (PR evidence).
/// </summary>
public class PostLogoutRedirectUriPersistenceTests
{
    private const string Table = "[dbo].[ApplicationPostLogoutRedirectUris]";
    private const string Return = "https://shop.example.com/signed-out";
    private const string LocalReturn = "http://localhost:3000/signed-out";

    private static object ApplicationRow() => new
    {
        Id = Guid.NewGuid(),
        Code = "SHOP",
        Name = "SHOP",
        IsActive = true,
        SessionTimeoutMinutes = 60,
        MaxConcurrentSessions = 5,
        AccessMode = (byte)1,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Guid.NewGuid()
    };

    private static RecordingDbConnectionFactory Reading() => new(
        affectedRows: 0,
        rowFor: command =>
            command.CommandText.Contains(Table, StringComparison.Ordinal) ? new { Uri = Return }
            : command.CommandText.Contains("[ApplicationRedirectUris]", StringComparison.Ordinal) ? null
            : ApplicationRow());

    [Fact]
    public async Task GetByCodeAsync_HydratesTheList_ForTheEndSessionEndpoint()
    {
        // GetByCodeAsync is what end-session reads: a list it does not hydrate
        // would ignore every registered return without an error.
        var db = Reading();

        var application = await new ApplicationRepository(db).GetByCodeAsync("SHOP", CancellationToken.None);

        application!.PostLogoutRedirectUris.Should().Equal(Return);
        var read = db.Commands.Single(c => c.CommandText.Contains(Table, StringComparison.Ordinal));
        read.CommandText.Should().MatchRegex(@"SELECT\s+\[Uri\]\s+FROM\s+\[dbo\]\.\[ApplicationPostLogoutRedirectUris\]\s+WHERE\s+\[ApplicationId\]\s*=\s*@ApplicationId");
        read.Parameters["ApplicationId"].Should().Be(application.Id);
    }

    [Fact]
    public async Task GetByIdAsync_HydratesTheList_ForTheConsole()
    {
        var db = Reading();

        var application = await new ApplicationRepository(db).GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        application!.PostLogoutRedirectUris.Should().Equal(Return);
    }

    [Fact]
    public async Task CreateAsync_InsertsEachReturnInTheApplicationsTransaction()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetPostLogoutRedirectUris([Return, LocalReturn], Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).CreateAsync(application, CancellationToken.None);

        var inserts = db.Commands.Where(c => c.CommandText.Contains($"INSERT INTO {Table}", StringComparison.Ordinal)).ToList();
        inserts.Select(c => c.Parameters["Uri"]).Should().Equal(Return, LocalReturn);
        inserts.Should().OnlyContain(c => c.InTransaction, "the application row and its allowlist land together");
        inserts.Should().OnlyContain(c => Equals(c.Parameters["ApplicationId"], application.Id));
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithNoReturns_WritesNoRow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).CreateAsync(TestHelpers.CreateApplication(code: "SHOP"), CancellationToken.None);

        db.Commands.Should().NotContain(c => c.CommandText.Contains(Table, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateAsync_RewritesTheListInsideTheUpdatesTransaction()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetPostLogoutRedirectUris([LocalReturn], Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).UpdateAsync(application, CancellationToken.None);

        var statements = db.Commands.Where(c => c.CommandText.Contains(Table, StringComparison.Ordinal)).ToList();
        statements.Should().HaveCount(2);
        statements[0].CommandText.Should().MatchRegex(@"DELETE\s+FROM\s+\[dbo\]\.\[ApplicationPostLogoutRedirectUris\]\s+WHERE\s+\[ApplicationId\]\s*=\s*@ApplicationId");
        statements[1].CommandText.Should().Contain($"INSERT INTO {Table}");
        statements[1].Parameters["Uri"].Should().Be(LocalReturn);
        statements.Should().OnlyContain(c => c.InTransaction,
            "a failed reinsert must not leave the application without its return addresses");
    }

    [Fact]
    public async Task UpdateAsync_WithAnEmptyList_OnlyDeletes()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).UpdateAsync(TestHelpers.CreateApplication(code: "SHOP"), CancellationToken.None);

        db.Commands.Where(c => c.CommandText.Contains(Table, StringComparison.Ordinal))
            .Should().ContainSingle()
            .Which.CommandText.Should().Contain("DELETE FROM");
    }
}
