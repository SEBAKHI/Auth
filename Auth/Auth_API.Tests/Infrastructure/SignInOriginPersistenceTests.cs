using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-178 through Dapper: the browser's user agent and device id on a code, and the sign-in's
/// device id on an IdP session, carried by every statement that writes or reads them. A recording
/// connection stands in, as in <see cref="ConfidentialClientPersistenceTests"/>.
/// </summary>
public class SignInOriginPersistenceTests
{
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";
    private const string Device = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private static object CodeRow(string? userAgent, string? deviceId) => new
    {
        Id = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), UserId = Guid.NewGuid(), CodeHash = "h",
        RedirectUri = "https://crm.example.com/bff/callback", CodeChallenge = "c", ExpiresAt = DateTime.UtcNow.AddMinutes(1),
        ConsumedAt = (DateTime?)null, CreatedAt = DateTime.UtcNow, IpAddress = "198.51.100.23",
        UserAgent = userAgent, DeviceId = deviceId
    };

    private static AuthorizationCode Code(string? userAgent, string? deviceId = Device) => AuthorizationCode.Create(
        Guid.NewGuid(), Guid.NewGuid(), "h", "https://crm.example.com/bff/callback", "c",
        TimeSpan.FromSeconds(60), "198.51.100.23", ScopeSet.FromStored("openid"),
        userAgent: userAgent, deviceId: deviceId);

    [Fact]
    public async Task CreateAsync_WritesTheBrowsersUserAgentAndDeviceId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(Code(ChromeOnWindows), CancellationToken.None);

        db.LastCommand!.CommandText.Should().MatchRegex(
            @"\[RequiresClientAuthentication\],\s*\[UserAgent\],\s*\[DeviceId\]\s*\)\s*VALUES[\s\S]*@RequiresClientAuthentication,\s*@UserAgent,\s*@DeviceId\s*\)");
        db.LastCommand.Parameters["UserAgent"].Should().Be(ChromeOnWindows);
        db.LastCommand.Parameters["DeviceId"].Should().Be(Device);
    }

    [Fact]
    public async Task CreateAsync_TruncatesAnOverLongUserAgentToTheColumn_RatherThanFailingTheAuthorize()
    {
        // TA10: the header is the browser's to choose; NVARCHAR(500) must not turn a long one into
        // a failed authorize.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(
            Code(new string('a', 600), deviceId: new string('d', 80)), CancellationToken.None);

        ((string)db.LastCommand!.Parameters["UserAgent"]!).Should().HaveLength(500);
        ((string)db.LastCommand.Parameters["DeviceId"]!).Should().HaveLength(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateAsync_WritesAMissingUserAgentAsEmpty_NeverAsNull(string? sent)
    {
        // NULL on a code means "written before the column", which the exchange answers from the
        // token request (AuthorizationCode.OriginFor). A code minted now writes '' when the
        // browser sent no header, all the way to the column.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(Code(sent), CancellationToken.None);

        db.LastCommand!.Parameters["UserAgent"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task CreateAsync_NeverSplitsASurrogatePairAtTheCut()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new AuthorizationCodeRepository(db).CreateAsync(Code(new string('a', 499) + "\U0001F600"), CancellationToken.None);

        ((string)db.LastCommand!.Parameters["UserAgent"]!).Should().Be(new string('a', 499),
            "the emoji does not fit whole, and half of it would be stored as a replacement character");
    }

    [Fact]
    public async Task EveryCodeRead_MapsAnEmptyUserAgentAsEmpty()
    {
        var applicationId = Guid.NewGuid();
        var reads = new (string Name, Func<AuthorizationCodeRepository, Task<AuthorizationCode?>> Read)[]
        {
            ("public consume", r => r.ConsumeByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), CancellationToken.None)),
            ("consume for client", r => r.ConsumeForClientAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), applicationId, CancellationToken.None)),
            ("get", r => r.GetByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), CancellationToken.None)),
        };

        foreach (var (name, read) in reads)
        {
            var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(string.Empty, Device));

            var code = await read(new AuthorizationCodeRepository(db));

            code!.UserAgent.Should().NotBeNull(name).And.BeEmpty(name);
        }
    }

    [Fact]
    public async Task EveryCodeRead_SelectsAndMapsTheBrowser()
    {
        var applicationId = Guid.NewGuid();
        var reads = new (string Name, Func<AuthorizationCodeRepository, Task<AuthorizationCode?>> Read)[]
        {
            ("public consume", r => r.ConsumeByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), CancellationToken.None)),
            ("consume for client", r => r.ConsumeForClientAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), applicationId, CancellationToken.None)),
            ("get", r => r.GetByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), CancellationToken.None)),
        };

        foreach (var (name, read) in reads)
        {
            var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(ChromeOnWindows, Device));

            var code = await read(new AuthorizationCodeRepository(db));

            db.LastCommand!.CommandText.Should().MatchRegex(@"\[UserAgent\],\s*(INSERTED\.)?\[DeviceId\]", name);
            code!.UserAgent.Should().Be(ChromeOnWindows, name);
            code.DeviceId.Should().Be(Device, name);
        }
    }

    [Fact]
    public async Task ACodeWrittenBeforeTheColumns_ReadsNoBrowser()
    {
        // E3: such a code answers from the token request, as before (AuthorizationCode.OriginFor).
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => CodeRow(null, null));

        var code = await new AuthorizationCodeRepository(db).GetByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "h"), CancellationToken.None);

        code!.UserAgent.Should().BeNull();
        code.DeviceId.Should().BeNull();
    }

    [Fact]
    public async Task TheSessionInsert_StillFitsClientTextToItsColumns_ThroughTheSharedHelper()
    {
        // The session row now carries the code's browser facts (OI-178); the user agent, device
        // name and device id are still cut to UserSessions.sql's widths, not refused by the insert.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var session = new UserSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", "198.51.100.23",
            new string('u', 600), DeviceType.Desktop, new string('d', 80), new string('n', 150), null, null,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7), DateTime.UtcNow, isActive: true, null, null);

        await new UserSessionRepository(db).CreateAsync(session, CancellationToken.None);

        ((string)db.LastCommand!.Parameters["UserAgent"]!).Should().HaveLength(500);
        ((string)db.LastCommand.Parameters["DeviceId"]!).Should().HaveLength(64);
        ((string)db.LastCommand.Parameters["DeviceName"]!).Should().HaveLength(100);
    }

    private static object IdpSessionRow(string? deviceId) => new
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "t", ExpiresAt = DateTime.UtcNow.AddDays(1),
        RevokedAt = (DateTime?)null, CreatedAt = DateTime.UtcNow, IpAddress = "198.51.100.23",
        DeviceInfo = ChromeOnWindows, IsPersistent = (bool?)false, LastActivityAt = (DateTime?)null,
        DeviceId = deviceId
    };

    [Fact]
    public async Task IdpSessionCreate_WritesTheSignInsDeviceId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var session = IdpSession.Create(
            Guid.NewGuid(), Guid.NewGuid(), "t", persistent: false, TimeSpan.FromHours(8), DateTime.UtcNow,
            "198.51.100.23", ChromeOnWindows, deviceId: Device);

        await new IdpSessionRepository(db).CreateAsync(session, CancellationToken.None);

        db.LastCommand!.CommandText.Should().MatchRegex(
            @"\[LastActivityAt\],\s*\[DeviceId\]\s*\)\s*VALUES[\s\S]*@LastActivityAt,\s*@DeviceId\s*\)");
        db.LastCommand.Parameters["DeviceId"].Should().Be(Device);
    }

    [Fact]
    public async Task IdpSessionCreate_FitsAnOverLongDeviceIdToTheColumn()
    {
        // A failed insert is swallowed by the sign-in and costs the person single sign-on silently.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var session = IdpSession.Create(
            Guid.NewGuid(), Guid.NewGuid(), "t", persistent: false, TimeSpan.FromHours(8), DateTime.UtcNow,
            "198.51.100.23", ChromeOnWindows, deviceId: new string('d', 80));

        await new IdpSessionRepository(db).CreateAsync(session, CancellationToken.None);

        ((string)db.LastCommand!.Parameters["DeviceId"]!).Should().HaveLength(64);
    }

    [Fact]
    public async Task EveryIdpSessionRead_SelectsAndMapsTheDeviceId()
    {
        // Authorize reads the session by its cookie's hash and copies the device id onto the code.
        foreach (var (name, read) in new (string, Func<IdpSessionRepository, Task<IdpSession?>>)[]
                 {
                     ("by token hash", r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "t"), CancellationToken.None)),
                     ("by id", r => r.GetByIdAsync(Guid.NewGuid(), CancellationToken.None)),
                 })
        {
            var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => IdpSessionRow(Device));

            var session = await read(new IdpSessionRepository(db));

            db.LastCommand!.CommandText.Should().MatchRegex(@"\[LastActivityAt\],\s*\[DeviceId\]\s+FROM", name);
            session!.DeviceId.Should().Be(Device, name);
        }
    }
}
