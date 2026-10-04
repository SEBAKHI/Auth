using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the refresh primitive's single write, <c>TryRotateAsync</c>.
/// <para>
/// Atomicity is three facts about the SQL, and losing any one of them silently
/// reopens a hole: without <c>AND [RevokedAt] IS NULL</c> two concurrent refreshes
/// both "win" and the token chain forks; without the affected-rows check the loser
/// creates a token anyway; without the insert inside the same transaction a failed
/// insert leaves the old token revoked with no replacement, and the retry looks
/// like theft. The test project has no database, so the source text and a recording
/// connection are the units under test — until S30b proves the same on SQL Server
/// under real concurrency.
/// </para>
/// </summary>
public class RefreshTokenRepositorySqlGuardTests
{
    private static string TryRotateBody()
    {
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "RefreshTokenRepository.cs"));
        var start = source.IndexOf("public async Task<bool> TryRotateAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "TryRotateAsync must exist in RefreshTokenRepository");
        var end = source.IndexOf("public async Task", start + 10, StringComparison.Ordinal);
        return source[start..end];
    }

    [Fact]
    public void TheRevokingUpdate_IsConditionedOnTheRowStillBeingLive()
    {
        var body = TryRotateBody();

        Regex.IsMatch(body, @"UPDATE\s+\[dbo\]\.\[RefreshTokens\][\s\S]*?WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[RevokedAt\]\s+IS\s+NULL")
            .Should().BeTrue("only the request that still finds the token live may rotate it");
    }

    [Fact]
    public void TheAffectedRowCount_DecidesTheWinner()
    {
        var body = TryRotateBody();

        body.Should().MatchRegex(@"var\s+revoked\s*=\s*await\s+connection\.ExecuteAsync");
        body.Should().MatchRegex(@"if\s*\(\s*revoked\s*!=\s*1\s*\)[\s\S]*?Rollback\(\)[\s\S]*?return\s+false");
    }

    [Fact]
    public void TheReplacement_IsCreatedInsideTheSameTransaction_BeforeTheCommit()
    {
        var body = TryRotateBody();

        var begin = body.IndexOf("BeginTransaction()", StringComparison.Ordinal);
        var create = body.IndexOf("sp_CreateRefreshToken", StringComparison.Ordinal);
        var commit = body.IndexOf("Commit()", StringComparison.Ordinal);

        begin.Should().BeGreaterThan(0);
        create.Should().BeGreaterThan(begin);
        commit.Should().BeGreaterThan(create);
        Regex.Matches(body, @"\btransaction,").Count.Should().Be(2, "both commands must be enlisted in the transaction");
        body.Should().NotContain(".Open(", "the factory returns an already-open connection");
    }

    private static (Auth.Domain.Entities.RefreshToken Old, Auth.Domain.Entities.RefreshToken Replacement) Pair()
    {
        var old = TestHelpers.CreateRefreshToken(expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());
        old.Revoke(old.UserId, TokenRevocationReasons.Rotated, "new-hash");
        var replacement = Auth.Domain.Entities.RefreshToken.Create(
            old.UserId, "new-hash", "jti", null, TimeSpan.FromDays(7), "127.0.0.1", null, old.SessionId);
        return (old, replacement);
    }

    [Fact]
    public async Task Winner_RevokesAndCreates_InOneCommittedTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var (old, replacement) = Pair();

        var won = await new RefreshTokenRepository(db).TryRotateAsync(old, replacement, CancellationToken.None);

        won.Should().BeTrue();
        db.Commands.Should().HaveCount(2);
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Commands[0].Parameters["ReplacedByTokenHash"].Should().Be("new-hash");
        db.Commands[1].CommandText.Should().Contain("sp_CreateRefreshToken");
        db.Commands[1].Parameters["SessionId"].Should().Be(old.SessionId);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Loser_CreatesNothing_AndRollsBack()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var (old, replacement) = Pair();

        var won = await new RefreshTokenRepository(db).TryRotateAsync(old, replacement, CancellationToken.None);

        won.Should().BeFalse();
        db.Commands.Should().ContainSingle();
        db.LastTransaction!.Committed.Should().BeFalse();
        db.LastTransaction.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task FailedInsert_NeverCommitsTheRevocation()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("sp_CreateRefreshToken", StringComparison.Ordinal)
                ? new InvalidOperationException("insert failed")
                : null);
        var (old, replacement) = Pair();

        var act = () => new RefreshTokenRepository(db).TryRotateAsync(old, replacement, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.LastTransaction!.Committed.Should().BeFalse(
            "an uncommitted transaction is rolled back on disposal, so the old token stays live");
    }

    // ------------------------------------------------------------------
    // OI-58: the granted scope rides the POSITIONAL procedure call. A site that
    // drops it loses the grant on that path only — for TryRotateAsync, every
    // rotated token and race sibling — and no handler test can see it.
    // ------------------------------------------------------------------

    private static string RepositorySource() => File.ReadAllText(Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "RefreshTokenRepository.cs"));

    private static string ProcedureSource() => File.ReadAllText(Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "StoredProcedures", "Authentication", "sp_CreateRefreshToken.sql"));

    [Fact]
    public void EveryCallOfTheProcedure_PassesScopeAsItsLastArgument()
    {
        var calls = Regex.Matches(RepositorySource(), @"EXEC \[dbo\]\.\[sp_CreateRefreshToken\][^""]*")
            .Select(match => match.Value)
            .ToList();

        calls.Should().HaveCount(2, "CreateAsync and TryRotateAsync each call the procedure once");
        calls.Should().OnlyContain(
            call => call.TrimEnd().EndsWith("@SessionId, @Scope", StringComparison.Ordinal),
            "the call is positional, so @Scope must sit where the procedure declares it: last");
    }

    [Fact]
    public void TheProcedure_DeclaresScopeLastWithADefault_AndInsertsIt()
    {
        var procedure = ProcedureSource();
        var parameters = procedure[..procedure.IndexOf("\nAS", StringComparison.Ordinal)];

        // Last, with a default: the previous API's eight-argument call keeps working.
        Regex.IsMatch(parameters, @"@Scope\s+NVARCHAR\(200\)\s*=\s*NULL\s*$")
            .Should().BeTrue("@Scope is the last parameter and defaults to NULL");
        Regex.IsMatch(procedure, @"\[ExpiresAt\],\s*\[Scope\]\s*\)").Should().BeTrue("the insert names the column");
        Regex.IsMatch(procedure, @"@ExpiresAt,\s*@Scope\s*\)").Should().BeTrue("and writes the parameter into it");
    }

    [Fact]
    public async Task CreateAsync_SendsTheGrant()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var token = Auth.Domain.Entities.RefreshToken.Create(
            Guid.NewGuid(), "hash", "jti", Guid.NewGuid(), TimeSpan.FromDays(7), "127.0.0.1", null,
            Guid.NewGuid(), "openid email");

        await new RefreshTokenRepository(db).CreateAsync(token, CancellationToken.None);

        db.LastCommand!.Parameters["Scope"].Should().Be("openid email");
    }

    [Fact]
    public async Task TryRotateAsync_SendsTheReplacementsGrant()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var old = TestHelpers.CreateRefreshToken(expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());
        old.Revoke(old.UserId, TokenRevocationReasons.Rotated, "new-hash");
        var replacement = Auth.Domain.Entities.RefreshToken.Create(
            old.UserId, "new-hash", "jti", Guid.NewGuid(), TimeSpan.FromDays(7), "127.0.0.1", null,
            old.SessionId, "openid phone");

        await new RefreshTokenRepository(db).TryRotateAsync(old, replacement, CancellationToken.None);

        db.Commands[1].Parameters["Scope"].Should().Be("openid phone");
    }

    [Fact]
    public async Task GetByTokenHashAsync_ReadsTheGrantBack()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TokenHash = "hash",
                JwtId = "jti",
                ApplicationId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                Scope = "openid profile"
            });

        var token = await new RefreshTokenRepository(db).GetByTokenHashAsync("hash", CancellationToken.None);

        token!.Scope.Should().Be("openid profile");
    }
}
