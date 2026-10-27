using Auth.Domain.Enums;
using System.Data.Common;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The pending authorize request table through Dapper (OI-145): what the
/// repository sends and what it reads back. A recording connection stands in for
/// the database, so Dapper builds the real command and parameters and maps a row
/// shaped like the table's into the real DTO. The same statements were run, as
/// written, against a throwaway LocalDB database (see the PR).
/// </summary>
public class AuthorizationRequestPersistenceTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly CapturingLogger<AuthorizationRequestRepository> _logger = new();

    private AuthorizationRequestRepository Repository(RecordingDbConnectionFactory db) => new(db, _logger);

    private static AuthorizationRequest Request() => AuthorizationRequest.Create(
        "handle-hash", Guid.NewGuid(), "SHOP", "code", "https://shop.example.com/cb", new string('a', 43), "S256",
        "rp state", "login create", 300, "openid email", createOrganization: true, TimeSpan.FromMinutes(60), Now,
        PresentationHints.From("ar-EG", "dark"));

    private static object Row(Guid id) => new
    {
        Id = id,
        HandleHash = "handle-hash",
        ApplicationId = Guid.NewGuid(),
        ClientId = "SHOP",
        ResponseType = "code",
        RedirectUri = "https://shop.example.com/cb",
        CodeChallenge = new string('a', 43),
        CodeChallengeMethod = "S256",
        State = "rp state",
        Prompt = "login",
        MaxAge = (int?)120,
        Scope = "openid profile",
        CreateOrganization = true,
        CreatedAt = Now,
        ExpiresAt = Now.AddMinutes(60),
        UiLocale = "fa",
        UiTheme = "light"
    };

    [Fact]
    public async Task CreateAsync_WritesEveryColumn()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var request = Request();

        await Repository(db).CreateAsync(request, CancellationToken.None);

        var command = db.LastCommand!;
        command.CommandText.Should().Contain("INSERT INTO [dbo].[AuthorizationRequests]");
        command.Parameters.Should().HaveCount(17);
        command.Parameters["Id"].Should().Be(request.Id);
        command.Parameters["HandleHash"].Should().Be("handle-hash");
        command.Parameters["ApplicationId"].Should().Be(request.ApplicationId);
        command.Parameters["ClientId"].Should().Be("SHOP");
        command.Parameters["ResponseType"].Should().Be("code");
        command.Parameters["RedirectUri"].Should().Be("https://shop.example.com/cb");
        command.Parameters["CodeChallenge"].Should().Be(new string('a', 43));
        command.Parameters["CodeChallengeMethod"].Should().Be("S256");
        command.Parameters["State"].Should().Be("rp state");
        command.Parameters["Prompt"].Should().Be("login create");
        command.Parameters["MaxAge"].Should().Be(300);
        command.Parameters["Scope"].Should().Be("openid email");
        command.Parameters["CreateOrganization"].Should().Be(true);
        command.Parameters["CreatedAt"].Should().Be(Now);
        command.Parameters["ExpiresAt"].Should().Be(Now.AddMinutes(60));
        command.Parameters["UiLocale"].Should().Be("ar", "the resolved language, never the tags as sent");
        command.Parameters["UiTheme"].Should().Be("dark");
        foreach (var name in command.Parameters.Keys)
        {
            command.CommandText.Should().Contain($"[{name}]").And.Contain($"@{name}");
        }
    }

    [Fact]
    public async Task CreateAsync_FirstRemovesMoreExpiredRowsThanItAdds()
    {
        // Every sessionless authorize writes a row, and the daily sweep stops at
        // its per-run ceiling: removing two expired rows per insert keeps the table
        // near the live ones under a spread-out flood, whatever that ceiling.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var request = Request();

        await Repository(db).CreateAsync(request, CancellationToken.None);

        db.Commands.Should().HaveCount(2);
        var purge = db.Commands[0];
        purge.CommandText.Should().Contain("DELETE TOP (@PurgeBatch) FROM [dbo].[AuthorizationRequests] WITH (READPAST)")
            .And.Contain("[ExpiresAt] < @Now");
        purge.Parameters["PurgeBatch"].Should().Be(2);
        purge.Parameters["Now"].Should().Be(Now, "judged on the application's clock, at the insert's own instant");
        db.Commands[1].CommandText.Should().Contain("INSERT INTO [dbo].[AuthorizationRequests]");
    }

    [Fact]
    public async Task CreateAsync_PurgeFails_StillStoresTheRequest_AndWarnsWithoutTheHash()
    {
        // F1 (REVIEW 68): the purge is housekeeping. A timeout or a deadlock with the
        // daily sweep must not turn the sign-in it rides on into a 500.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("DELETE TOP (@PurgeBatch)")
                ? new TestDbException("deadlock victim")
                : null);

        await Repository(db).CreateAsync(Request(), CancellationToken.None);

        db.Commands.Should().HaveCount(2);
        db.Commands[1].CommandText.Should().Contain("INSERT INTO [dbo].[AuthorizationRequests]",
            "the insert runs after a failed purge");
        var warning = _logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Exception.Should().BeOfType<TestDbException>();
        (warning.Message + string.Join(" ", warning.Values)).Should().NotContain("handle-hash");
    }

    [Fact]
    public async Task CreateAsync_InsertFails_Throws()
    {
        // The request itself is not housekeeping: a row that could not be stored is
        // an error the central handler reports, never a silent fallback to returnTo.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("INSERT INTO [dbo].[AuthorizationRequests]")
                ? new TestDbException("insert refused")
                : null);

        var act = () => Repository(db).CreateAsync(Request(), CancellationToken.None);

        await act.Should().ThrowAsync<TestDbException>();
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_PurgeCancelled_IsNotSwallowed()
    {
        // Cancellation is neither caught nor logged as a failure.
        using var cancelled = new CancellationTokenSource();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command =>
            {
                if (!command.CommandText.Contains("DELETE TOP (@PurgeBatch)")) return null;
                cancelled.Cancel();
                return new TestDbException("operation cancelled by user");
            });

        var act = () => Repository(db).CreateAsync(Request(), cancelled.Token);

        await act.Should().ThrowAsync<TestDbException>();
        db.Commands.Should().ContainSingle("no insert after a cancelled purge");
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLiveByHandleHashAsync_FiltersOnTheHashAndTheGivenInstant_AndMapsTheRow()
    {
        var id = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => Row(id));

        var request = await Repository(db)
            .GetLiveByHandleHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationRequestHandle, "handle-hash"), Now, CancellationToken.None);

        var command = db.LastCommand!;
        command.CommandText.Should().Contain("[HandleHash] IN (@Hashes1)")
            .And.Contain("[ExpiresAt] > @Now",
                "an expired handle resumes nothing, judged on the application's clock, not the server's");
        command.Parameters["Hashes1"].Should().Be("handle-hash");
        command.Parameters["Now"].Should().Be(Now);

        request.Should().NotBeNull();
        request!.Id.Should().Be(id);
        request.ClientId.Should().Be("SHOP");
        request.State.Should().Be("rp state");
        request.Prompt.Should().Be("login");
        request.MaxAge.Should().Be(120);
        request.Scope.Should().Be("openid profile");
        request.CreateOrganization.Should().BeTrue();
        request.ExpiresAt.Should().Be(Now.AddMinutes(60));
        request.Hints.Language.Should().Be("fa");
        request.Hints.Theme.Should().Be("light");
    }

    [Fact]
    public async Task CreateAsync_NoHints_WritesNulls()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var request = AuthorizationRequest.Create(
            "handle-hash", Guid.NewGuid(), "SHOP", "code", "https://shop.example.com/cb", new string('a', 43), "S256",
            null, null, null, "openid", createOrganization: false, TimeSpan.FromMinutes(60), Now);

        await Repository(db).CreateAsync(request, CancellationToken.None);

        db.LastCommand!.Parameters["UiLocale"].Should().BeNull();
        db.LastCommand.Parameters["UiTheme"].Should().BeNull();
    }

    [Fact]
    public async Task GetLiveByHandleHashAsync_RowWithoutHints_ReadsAsNone()
    {
        // A row written before the columns existed, or by a request that named none.
        var id = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new
        {
            Id = id,
            HandleHash = "handle-hash",
            ApplicationId = Guid.NewGuid(),
            ClientId = "SHOP",
            ResponseType = "code",
            RedirectUri = "https://shop.example.com/cb",
            CodeChallenge = new string('a', 43),
            CodeChallengeMethod = "S256",
            State = (string?)null,
            Prompt = (string?)null,
            MaxAge = (int?)null,
            Scope = "openid",
            CreateOrganization = false,
            CreatedAt = Now,
            ExpiresAt = Now.AddMinutes(60),
            UiLocale = (string?)null,
            UiTheme = (string?)null
        });

        var request = await Repository(db)
            .GetLiveByHandleHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationRequestHandle, "handle-hash"), Now, CancellationToken.None);

        request!.Hints.Should().BeSameAs(PresentationHints.None);
    }

    [Fact]
    public async Task GetLiveByHandleHashAsync_NoRow_ReturnsNull()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var request = await Repository(db)
            .GetLiveByHandleHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationRequestHandle, "handle-hash"), Now, CancellationToken.None);

        request.Should().BeNull();
    }

    [Fact]
    public async Task ClearCreateOrganizationAsync_ClearsTheOneRow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var id = Guid.NewGuid();

        await Repository(db).ClearCreateOrganizationAsync(id, CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[CreateOrganization] = 0").And.Contain("WHERE [Id] = @Id");
        db.LastCommand.Parameters["Id"].Should().Be(id);
    }

    [Fact]
    public async Task CleanupExpiredAsync_DeletesOneBoundedBatchByExpiry()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 7);

        var deleted = await Repository(db)
            .CleanupExpiredAsync(Now, 500, CancellationToken.None);

        deleted.Should().Be(7);
        db.LastCommand!.CommandText.Should().Contain("DELETE TOP (@BatchSize) FROM [dbo].[AuthorizationRequests]")
            .And.Contain("[ExpiresAt] < @OlderThan");
        db.LastCommand.Parameters["BatchSize"].Should().Be(500);
        db.LastCommand.Parameters["OlderThan"].Should().Be(Now);
    }

    [Fact]
    public void TheEntity_IsLiveUntilItsExpiry_AndBelongsToItsOwnClientOnly()
    {
        var request = Request();

        request.IsLive(Now).Should().BeTrue();
        request.IsLive(Now.AddMinutes(60)).Should().BeFalse("expiry is fixed at creation and never slides");
        request.IsFor("SHOP").Should().BeTrue();
        request.IsFor("shop").Should().BeFalse();
        request.IsFor(null).Should().BeFalse();

        request.DropOrganizationRequest();
        request.CreateOrganization.Should().BeFalse();
    }

    [Fact]
    public void TheEntity_RefusesAProgrammingError()
    {
        var create = () => AuthorizationRequest.Create(
            "", Guid.NewGuid(), "SHOP", "code", "https://shop.example.com/cb", new string('a', 43), "S256",
            null, null, null, "openid", createOrganization: false, TimeSpan.FromMinutes(60), Now);
        var noLifetime = () => AuthorizationRequest.Create(
            "hash", Guid.NewGuid(), "SHOP", "code", "https://shop.example.com/cb", new string('a', 43), "S256",
            null, null, null, "openid", createOrganization: false, TimeSpan.Zero, Now);

        create.Should().Throw<ArgumentException>();
        noLifetime.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class TestDbException(string message) : DbException(message);
}
