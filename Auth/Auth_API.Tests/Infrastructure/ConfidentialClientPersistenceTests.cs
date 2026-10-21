using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-171 through Dapper: the server-side redirect list, the key's purpose and the binding flag of
/// codes and refresh tokens, each carried by every statement that reads or writes it. A recording
/// connection stands in, as in <see cref="PostLogoutRedirectUriPersistenceTests"/>; the same
/// statements also ran against a throwaway LocalDB built from the DACPAC (PR evidence).
/// </summary>
public class ConfidentialClientPersistenceTests
{
    private const string Table = "[dbo].[ApplicationConfidentialRedirectUris]";
    private const string ServerSide = "https://shop.example.com/bff/callback";

    // --- the server-side redirect list ---

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
            command.CommandText.Contains(Table, StringComparison.Ordinal) ? new { Uri = ServerSide }
            : command.CommandText.Contains("RedirectUris]", StringComparison.Ordinal) ? null
            : ApplicationRow());

    [Fact]
    public async Task GetByCodeAsync_HydratesTheServerSideList_ForAuthorize()
    {
        var db = Reading();

        var application = await new ApplicationRepository(db).GetByCodeAsync("SHOP", CancellationToken.None);

        application!.ConfidentialRedirectUris.Should().Equal(ServerSide);
        application.RedirectUris.Should().BeEmpty("the server-side list is read from its own table only");
        var read = db.Commands.Single(c => c.CommandText.Contains(Table, StringComparison.Ordinal));
        read.CommandText.Should().MatchRegex(@"SELECT\s+\[Uri\]\s+FROM\s+\[dbo\]\.\[ApplicationConfidentialRedirectUris\]\s+WHERE\s+\[ApplicationId\]\s*=\s*@ApplicationId");
    }

    [Fact]
    public async Task GetByIdAsync_HydratesTheServerSideList_ForTheConsole()
    {
        var application = await new ApplicationRepository(Reading()).GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        application!.ConfidentialRedirectUris.Should().Equal(ServerSide);
    }

    [Fact]
    public async Task CreateAsync_InsertsTheServerSideList_IntoItsOwnTable_InTheApplicationsTransaction()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetConfidentialRedirectUris([ServerSide], Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).CreateAsync(application, CancellationToken.None);

        var insert = db.Commands.Should().ContainSingle(c => c.CommandText.Contains($"INSERT INTO {Table}", StringComparison.Ordinal)).Subject;
        insert.Parameters["Uri"].Should().Be(ServerSide);
        insert.InTransaction.Should().BeTrue();
        db.Commands.Where(c => c.CommandText.Contains("INSERT INTO [dbo].[ApplicationRedirectUris]", StringComparison.Ordinal))
            .Should().BeEmpty("a server-side address never lands in the public table, which an older API reads (TB11)");
    }

    [Fact]
    public async Task UpdateAsync_RewritesTheServerSideList_InsideTheUpdatesTransaction()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetConfidentialRedirectUris([ServerSide], Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).UpdateAsync(application, CancellationToken.None);

        var statements = db.Commands.Where(c => c.CommandText.Contains(Table, StringComparison.Ordinal)).ToList();
        statements.Should().HaveCount(2);
        statements[0].CommandText.Should().Contain("DELETE FROM");
        statements[1].Parameters["Uri"].Should().Be(ServerSide);
        statements.Should().OnlyContain(c => c.InTransaction);
    }

    // --- the key's purpose ---

    [Theory]
    [InlineData(MachineKeyPurpose.Machine, (byte)1)]
    [InlineData(MachineKeyPurpose.SignIn, (byte)2)]
    public async Task TryCreateAsync_StoresThePurpose_AndCapsPerPurpose(MachineKeyPurpose purpose, byte stored)
    {
        var key = ApplicationMachineKey.Create(Guid.NewGuid(), "kid", "{}", null, Guid.NewGuid(), DateTime.UtcNow, purpose).Value;
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationMachineKeyRepository(db).TryCreateAsync(key, MachineClients.MaxActiveKeysPerApplication, CancellationToken.None);

        db.LastCommand!.Parameters["Purpose"].Should().Be(stored);
        db.LastCommand.CommandText.Should().MatchRegex(@"\[Purpose\]\s*=\s*@Purpose\s*\)\s*<\s*@MaxActiveKeys");
    }

    [Fact]
    public async Task GetActiveByKidAsync_ReadsThePurpose()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new
        {
            Id = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), Kid = "kid", Algorithm = "ES256", PublicKeyJwk = "{}",
            CreatedAt = DateTime.UtcNow, CreatedBy = Guid.NewGuid(), Purpose = (byte)2, ApplicationCode = "SHOP"
        });

        var credential = await new ApplicationMachineKeyRepository(db).GetActiveByKidAsync("kid", CancellationToken.None);

        credential!.Key.Purpose.Should().Be(MachineKeyPurpose.SignIn);
        db.LastCommand!.CommandText.Should().Contain("k.[Purpose]");
    }

    [Fact]
    public void Create_RefusesAPurposeThatDoesNotExist()
    {
        ApplicationMachineKey.Create(Guid.NewGuid(), "kid", "{}", null, Guid.NewGuid(), DateTime.UtcNow, (MachineKeyPurpose)7)
            .FirstError.Code.Should().Be("MachineKey.PurposeInvalid");
    }

    // --- the code's flag ---

    [Fact]
    public void ThePublicConsume_TakesNoServerSideCode_AndTheAuthenticatedOne_OnlyItsApplicationsCodes()
    {
        AuthorizationCodeRepository.ConsumePublicStatement.Should().MatchRegex(
            @"WHERE\s+\[CodeHash\]\s+IN\s+@Hashes\s+AND\s+\[ConsumedAt\]\s+IS\s+NULL\s+AND\s+\[RequiresClientAuthentication\]\s+IS\s+NULL\s*$");
        AuthorizationCodeRepository.ConsumeForClientStatement.Should().MatchRegex(
            @"WHERE\s+\[CodeHash\]\s+IN\s+@Hashes\s+AND\s+\[ConsumedAt\]\s+IS\s+NULL\s+AND\s+\[ApplicationId\]\s*=\s*@ApplicationId\s*$");
        AuthorizationCodeRepository.ConsumeForClientStatement.Should().MatchRegex(
            @"SET\s+\[ConsumedAt\]\s*=\s*GETUTCDATE\(\),\s*\[RequiresClientAuthentication\]\s*=\s*1\s+OUTPUT",
            "a code the server redeemed with its key is the server's from then on (CA2b for CA5)");
        AuthorizationCodeRepository.ConsumePublicStatement.Should().NotMatchRegex(@"\[RequiresClientAuthentication\]\s*=\s*1");
        foreach (var statement in new[] { AuthorizationCodeRepository.ConsumePublicStatement, AuthorizationCodeRepository.ConsumeForClientStatement })
        {
            statement.Should().Contain("INSERTED.[RequiresClientAuthentication]");
            Regex.Matches(statement, @"\bUPDATE\b").Should().ContainSingle("one atomic statement decides who consumes");
        }
    }

    private static object CodeRow(Guid applicationId, bool? stored) => new
    {
        Id = Guid.NewGuid(), ApplicationId = applicationId, UserId = Guid.NewGuid(), CodeHash = "h",
        RedirectUri = "https://x.example.com/cb", CodeChallenge = "c", ExpiresAt = DateTime.UtcNow.AddMinutes(1),
        ConsumedAt = (DateTime?)null, CreatedAt = DateTime.UtcNow, RequiresClientAuthentication = stored
    };

    [Fact]
    public async Task ConsumeByCodeHashAsync_RunsThePublicStatement_AndConsumeForClientAsync_TheAuthenticatedOne()
    {
        // F1: the constants are pinned above; this pins that each method sends its own.
        var applicationId = Guid.NewGuid();
        var publicDb = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(applicationId, null));
        var clientDb = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(applicationId, true));

        await new AuthorizationCodeRepository(publicDb).ConsumeByCodeHashAsync(FakeCredentialHasher.CandidatesOf("h"), CancellationToken.None);
        await new AuthorizationCodeRepository(clientDb).ConsumeForClientAsync(FakeCredentialHasher.CandidatesOf("h"), applicationId, CancellationToken.None);

        // As Dapper sends it: the candidate list expanded in place of @Hashes.
        publicDb.Commands.Should().ContainSingle().Which.CommandText.Should().Be(
            AuthorizationCodeRepository.ConsumePublicStatement.Replace("IN @Hashes", "IN (@Hashes1)"));
        publicDb.LastCommand!.Parameters["Hashes1"].Should().Be("h");
        clientDb.Commands.Should().ContainSingle().Which.CommandText.Should().Be(
            AuthorizationCodeRepository.ConsumeForClientStatement.Replace("IN @Hashes", "IN (@Hashes1)"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public async Task GetByCodeHashAsync_SelectsAndMapsTheFlag(bool? stored, bool expected)
    {
        // The replay branch decides CA2b on this read: a code read without its flag would let an
        // unauthenticated replay of a server-side code revoke what it issued.
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => CodeRow(Guid.NewGuid(), stored));

        var code = await new AuthorizationCodeRepository(db).GetByCodeHashAsync(FakeCredentialHasher.CandidatesOf("h"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().MatchRegex(@"SELECT[\s\S]*\[RequiresClientAuthentication\][\s\S]*FROM\s+\[dbo\]\.\[AuthorizationCodes\]");
        code!.RequiresClientAuthentication.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, null)]
    public async Task CreateAsync_WritesTheCodesFlag_OneOrNull(bool serverSide, bool? stored)
    {
        var code = AuthorizationCode.Create(Guid.NewGuid(), Guid.NewGuid(), "h", "https://x.example.com/cb", "c",
            TimeSpan.FromSeconds(60), null, ScopeSet.FromStored("openid"), requiresClientAuthentication: serverSide);
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(code, CancellationToken.None);

        // OI-178's columns follow the flag in the list; SignInOriginPersistenceTests pins them.
        db.LastCommand!.CommandText.Should().MatchRegex(@"\[IdpSessionId\],\s*\[RequiresClientAuthentication\][^)]*\)\s*VALUES");
        db.LastCommand.Parameters["RequiresClientAuthentication"].Should().Be(stored);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public async Task ConsumeForClientAsync_ReadsTheFlag(bool? stored, bool expected)
    {
        var applicationId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId, UserId = Guid.NewGuid(), CodeHash = "h",
            RedirectUri = "https://x.example.com/cb", CodeChallenge = "c", ExpiresAt = DateTime.UtcNow.AddMinutes(1),
            ConsumedAt = (DateTime?)DateTime.UtcNow, CreatedAt = DateTime.UtcNow, RequiresClientAuthentication = stored
        });

        var code = await new AuthorizationCodeRepository(db).ConsumeForClientAsync(FakeCredentialHasher.CandidatesOf("h"), applicationId, CancellationToken.None);

        code!.RequiresClientAuthentication.Should().Be(expected);
        db.LastCommand!.Parameters["ApplicationId"].Should().Be(applicationId);
    }

    // --- the refresh token's flag ---

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, null)]
    public async Task CreateAndRotate_WriteTheRefreshTokensFlag_OneOrNull(bool bound, bool? stored)
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash", "jti", Guid.NewGuid(), TimeSpan.FromDays(7), null, null,
            requiresClientAuthentication: bound);
        var old = TestHelpers.CreateRefreshToken();
        old.Revoke(null, TokenRevocationReasons.Rotated, "hash");

        var created = new RecordingDbConnectionFactory(affectedRows: 1);
        await new RefreshTokenRepository(created).CreateAsync(token, CancellationToken.None);
        var rotated = new RecordingDbConnectionFactory(affectedRows: 1);
        await new RefreshTokenRepository(rotated).TryRotateAsync(old, token, CancellationToken.None);

        created.LastCommand!.Parameters["RequiresClientAuthentication"].Should().Be(stored);
        rotated.Commands.Single(c => c.CommandText.Contains("sp_CreateRefreshToken", StringComparison.Ordinal))
            .Parameters["RequiresClientAuthentication"].Should().Be(stored);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public async Task GetByTokenHashAsync_ReadsTheFlag(bool? stored, bool expected)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "hash", JwtId = "jti",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1), RequiresClientAuthentication = stored
        });

        var token = await new RefreshTokenRepository(db).GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("hash"), CancellationToken.None);

        token!.RequiresClientAuthentication.Should().Be(expected);
    }

    // --- readiness: an API deployed ahead of its DACPAC says what is missing ---

    [Theory]
    [InlineData("OBJECT_ID('dbo.ApplicationConfidentialRedirectUris', 'U')", "ApplicationConfidentialRedirectUris table")]
    [InlineData("COL_LENGTH('dbo.ApplicationMachineKeys', 'Purpose')", "ApplicationMachineKeys.Purpose")]
    [InlineData("COL_LENGTH('dbo.AuthorizationCodes', 'RequiresClientAuthentication')", "AuthorizationCodes.RequiresClientAuthentication")]
    [InlineData("COL_LENGTH('dbo.RefreshTokens', 'RequiresClientAuthentication')", "RefreshTokens.RequiresClientAuthentication")]
    [InlineData("[name] = '@RequiresClientAuthentication'", "sp_CreateRefreshToken.@RequiresClientAuthentication")]
    public void Readiness_NamesEachPieceOfTheBatch(string sql, string name)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains(sql, StringComparison.Ordinal) && expectation.Name.Contains(name, StringComparison.Ordinal));
    }
}
