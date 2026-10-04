using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The scope columns through Dapper (OI-58, B5 and B9): what the repositories send
/// and what they read back. The test project has no database, so a recording
/// connection stands in for it: Dapper builds the real command and parameters,
/// and maps a row shaped like the table's into the real DTO.
/// </summary>
public class GrantedScopePersistenceTests
{
    private static AuthorizationCode Code(string scope) => AuthorizationCode.Create(
        Guid.NewGuid(), Guid.NewGuid(), "code-hash", "https://app.example.com/cb", new string('a', 43),
        TimeSpan.FromSeconds(60), "127.0.0.1", ScopeSet.FromStored(scope));

    private static object CodeRow(string? scope) => new
    {
        Id = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        CodeHash = "code-hash",
        RedirectUri = "https://app.example.com/cb",
        CodeChallenge = new string('a', 43),
        ExpiresAt = DateTime.UtcNow.AddSeconds(60),
        ConsumedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        IpAddress = "127.0.0.1",
        Scope = scope
    };

    [Fact]
    public async Task AuthorizationCode_CreateAsync_WritesTheGrant()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(Code("openid email phone"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[Scope]").And.Contain("@Scope");
        db.LastCommand.Parameters["Scope"].Should().Be("openid email phone");
    }

    [Fact]
    public async Task AuthorizationCode_ConsumeByCodeHashAsync_ReturnsTheGrantFromItsOutputList()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow("openid profile"));

        var code = await new AuthorizationCodeRepository(db).ConsumeByCodeHashAsync("code-hash", CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("INSERTED.[Scope]",
            "the consuming UPDATE is the exchange's only read of the code; a column missing from its " +
            "OUTPUT list comes back null and the exchange would grant openid alone");
        code!.Scope.Should().Be("openid profile");
        code.GrantedScopes.Value.Should().Be("openid profile");
    }

    [Fact]
    public async Task AuthorizationCode_GetByCodeHashAsync_ReturnsTheGrant()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => CodeRow("openid phone"));

        var code = await new AuthorizationCodeRepository(db).GetByCodeHashAsync("code-hash", CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[Scope]");
        code!.Scope.Should().Be("openid phone");
    }

    [Fact]
    public async Task AuthorizationCode_RowWithoutGrant_IsOpenIdOnly()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(null));

        var code = await new AuthorizationCodeRepository(db).ConsumeByCodeHashAsync("code-hash", CancellationToken.None);

        code!.Scope.Should().BeNull();
        code.GrantedScopes.Should().Be(ScopeSet.OpenIdOnly);
    }

    private static object ApplicationRow(string? allowedScopes) => new
    {
        Id = Guid.NewGuid(),
        Code = "EDIS",
        Name = "EDIS",
        IsActive = true,
        SessionTimeoutMinutes = 60,
        MaxConcurrentSessions = 5,
        AccessMode = (byte)1,
        AllowedScopes = allowedScopes,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Guid.NewGuid()
    };

    [Theory]
    [InlineData("profile email phone", "openid profile email phone")]
    [InlineData(null, "openid")]
    public async Task Application_GetByCodeAsync_ReadsTheAllowedScopes(string? stored, string expected)
    {
        // GetByCodeAsync is what /auth/authorize reads: a missing column here would
        // drop every requested scope from every grant without an error.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: command => command.CommandText.Contains("[ApplicationRedirectUris]", StringComparison.Ordinal)
                ? null
                : ApplicationRow(stored));

        var application = await new ApplicationRepository(db).GetByCodeAsync("EDIS", CancellationToken.None);

        application!.AllowedScopes.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "phone", "email" }, "email phone")]
    [InlineData(new[] { "openid" }, null)]
    [InlineData(new string[0], null)]
    public async Task Application_CreateAndUpdate_StoreTheCanonicalOptionalScopesOrNull(string[] chosen, string? stored)
    {
        var application = TestHelpers.CreateApplication(code: "EDIS");
        application.SetAllowedScopes(chosen, Guid.NewGuid()).IsError.Should().BeFalse();

        var createDb = new RecordingDbConnectionFactory(affectedRows: 1);
        await new ApplicationRepository(createDb).CreateAsync(application, CancellationToken.None);
        var updateDb = new RecordingDbConnectionFactory(affectedRows: 1);
        await new ApplicationRepository(updateDb).UpdateAsync(application, CancellationToken.None);

        createDb.Commands[0].Parameters["AllowedScopes"].Should().Be(stored);
        updateDb.Commands[0].Parameters["AllowedScopes"].Should().Be(stored);
    }
}
