using System.Text.Json;
using Auth.Application.Features.Authentication.GetOidcUserInfo;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Authentication.OidcUserInfo;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// UserInfo answers from the user's row, filtered by the token's granted scopes (X11, R1). The
/// query always comes from a real token, minted with a scope and validated by the userinfo
/// scheme, exactly as the action reads it; and every assertion reads the serialized JSON, the
/// form a relying party receives, because only that shows a boolean as a boolean and an absent
/// member as absent.
/// </summary>
public sealed class GetOidcUserInfoQueryHandlerTests : IDisposable
{
    private const string Phone = "+971 (50) 123-4567";
    private const string PictureKey = "avatars/user.png";
    private const string PictureUrl = "https://auth.example.com/uploads/images/avatars/user.png";

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IImageUrlComposer> _images = new();

    public GetOidcUserInfoQueryHandlerTests()
    {
        _images.Setup(c => c.Compose(PictureKey)).Returns(PictureUrl);
    }

    public void Dispose() => _tokens.Dispose();

    private static User CreateUser(
        string? phone = Phone,
        bool phoneConfirmed = false,
        bool emailConfirmed = true,
        string? timeZone = "Asia/Dubai",
        string? picture = PictureKey,
        UserStatus status = UserStatus.Active,
        DateTime? lockoutEnd = null) =>
        new(
            id: Guid.NewGuid(),
            email: "user@example.com",
            normalizedEmail: "USER@EXAMPLE.COM",
            passwordHash: "hash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: phone,
            status: status,
            emailConfirmed: emailConfirmed,
            phoneConfirmed: phoneConfirmed,
            twoFactorEnabled: false,
            failedLoginAttempts: 0,
            lockoutEnd: lockoutEnd,
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "ar",
            timeZone: timeZone,
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.Empty,
            modifiedAt: null,
            modifiedBy: null,
            profileImageUrl: picture);

    /// <summary>
    /// Mints an application token with <paramref name="scope"/> (null: no scope claim at all),
    /// validates it with the userinfo scheme, reads the query from the principal as the action
    /// does, runs the handler and serializes the answer.
    /// </summary>
    private async Task<(bool IsError, string? Code, JsonElement Body)> AskAsync(User? stored, User subject, string? scope)
    {
        _users.Setup(r => r.GetByIdAsync(subject.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var principal = await _tokens.UserInfoPrincipalAsync(_tokens.ForApplication(subject, scope));
        var handler = new GetOidcUserInfoQueryHandler(_users.Object, _images.Object);

        var result = await handler.Handle(GetOidcUserInfoQuery.FromPrincipal(principal), CancellationToken.None);
        if (result.IsError)
        {
            return (true, result.FirstError.Code, default);
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        return (false, null, document.RootElement.Clone());
    }

    private async Task<JsonElement> BodyAsync(User user, string? scope)
    {
        var answer = await AskAsync(user, user, scope);
        answer.IsError.Should().BeFalse();
        return answer.Body;
    }

    private static IReadOnlyList<string> Members(JsonElement body) =>
        body.EnumerateObject().Select(p => p.Name).ToList();

    // --- The scopes decide the members ---

    public static TheoryData<string?, string[]> ScopeCases => new()
    {
        { "openid", ["sub"] },
        { "openid profile", ["sub", "name", "given_name", "family_name", "locale", "zoneinfo", "picture"] },
        { "openid email", ["sub", "email", "email_verified"] },
        { "openid phone", ["sub", "phone_number", "phone_number_verified"] },
        {
            "openid profile email phone",
            ["sub", "name", "given_name", "family_name", "locale", "zoneinfo", "picture",
             "email", "email_verified", "phone_number", "phone_number_verified"]
        },
        // A token minted before scopes were deployed carries no claim: openid only.
        { null, ["sub"] },
        // An unknown word cannot be written by the server; if one appears it grants nothing.
        { "openid admin", ["sub"] },
    };

    [Fact]
    public void ScopeCases_IsNotEmpty() => ScopeCases.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(ScopeCases))]
    public async Task Handle_GrantedScopes_ReturnExactlyTheirMembers(string? scope, string[] expected)
    {
        var user = CreateUser();

        var body = await BodyAsync(user, scope);

        Members(body).Should().BeEquivalentTo(expected);
        body.GetProperty("sub").GetString().Should().Be(user.Id.ToString());
    }

    [Fact]
    public async Task Handle_EveryScope_ReturnsTheRowsValues()
    {
        var user = CreateUser();

        var body = await BodyAsync(user, "openid profile email phone");

        body.GetProperty("name").GetString().Should().Be("Test User");
        body.GetProperty("given_name").GetString().Should().Be("Test");
        body.GetProperty("family_name").GetString().Should().Be("User");
        body.GetProperty("locale").GetString().Should().Be("ar");
        body.GetProperty("zoneinfo").GetString().Should().Be("Asia/Dubai");
        body.GetProperty("picture").GetString().Should().Be(PictureUrl);
        body.GetProperty("email").GetString().Should().Be("user@example.com");
        body.GetProperty("email_verified").ValueKind.Should().Be(JsonValueKind.True);
        body.GetProperty("phone_number").GetString().Should().Be(Phone);
    }

    [Fact]
    public async Task Handle_NeverReturnsThePlatformsAuthority()
    {
        // The application token carries roles, permissions, org_perm and the organization
        // claims (asserted first, so the absence below is not vacuous); an application must never
        // read them, or the scope, back from here.
        var user = CreateUser();
        var principal = await _tokens.UserInfoPrincipalAsync(_tokens.ForApplication(user, "openid profile email phone"));
        foreach (var claim in new[] { "roles", "permissions", "org_perm", "org_id", "org_name", "scope", "sid", "jti", "timezone", "theme" })
        {
            principal.HasClaim(c => c.Type == claim).Should().BeTrue("the test token must carry {0}", claim);
        }

        var body = await BodyAsync(user, "openid profile email phone");

        Members(body).Should().NotContain(
            ["roles", "permissions", "org_perm", "org_id", "org_name", "theme", "sid", "jti", "scope", "mfaRequirement", "timezone"]);
    }

    // --- Phone ---

    [Theory]
    [InlineData(false, JsonValueKind.False)]
    [InlineData(true, JsonValueKind.True)]
    public async Task Handle_PhoneScope_WritesTheVerifiedFlagAsAJsonBoolean(bool confirmed, JsonValueKind expected)
    {
        var body = await BodyAsync(CreateUser(phoneConfirmed: confirmed), "openid phone");

        // A boolean, not the string "false": a relying party testing the value for truth would
        // read the string "false" as true.
        body.GetProperty("phone_number_verified").ValueKind.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_PhoneScope_UserWithoutAPhone_WritesNeitherPhoneMember()
    {
        var body = await BodyAsync(CreateUser(phone: null), "openid phone");

        Members(body).Should().Equal("sub");
    }

    [Fact]
    public async Task Handle_PhoneScope_DigitsOnlyPhone_StaysAJsonStringWithItsLeadingZero()
    {
        var body = await BodyAsync(CreateUser(phone: "0501234567"), "openid phone");

        body.GetProperty("phone_number").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("phone_number").GetString().Should().Be("0501234567");
    }

    [Fact]
    public async Task Handle_EmailScope_UnconfirmedEmail_WritesFalse()
    {
        var body = await BodyAsync(CreateUser(emailConfirmed: false), "openid email");

        body.GetProperty("email_verified").ValueKind.Should().Be(JsonValueKind.False);
    }

    // --- zoneinfo: "UTC" is the column default and means "automatic" ---

    [Theory]
    [InlineData("UTC", null)]
    [InlineData("Etc/UTC", "Etc/UTC")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Europe/Istanbul", "Europe/Istanbul")]
    public async Task Handle_ProfileScope_ZoneInfo(string? stored, string? expected)
    {
        var body = await BodyAsync(CreateUser(timeZone: stored), "openid profile");

        if (expected is null)
        {
            body.TryGetProperty("zoneinfo", out _).Should().BeFalse();
        }
        else
        {
            body.GetProperty("zoneinfo").GetString().Should().Be(expected);
        }
    }

    // --- picture: an absolute http(s) URL with no credentials, or nothing ---

    [Fact]
    public async Task Handle_ProfileScope_AbsoluteHttpsPicture_IsWrittenAsItsAbsoluteUri()
    {
        _images.Setup(c => c.Compose("odd.png")).Returns("HTTPS://Auth.Example.com/uploads/a b.png");

        var body = await BodyAsync(CreateUser(picture: "odd.png"), "openid profile");

        body.GetProperty("picture").GetString().Should().Be("https://auth.example.com/uploads/a%20b.png");
    }

    /// <summary>
    /// What the composer may hand back that must not become a picture: the relative path the
    /// default image base produces, nothing, schemes a relying party must never load, the rooted
    /// path as Linux parses it, and an address carrying credentials.
    /// </summary>
    public static TheoryData<string?> UnusablePictures => new()
    {
        "/uploads/images/a.png",
        null,
        "",
        "ftp://h/a.png",
        "javascript:alert(1)",
        "file:///etc/passwd",
        "https://u:p@h/a.png",
    };

    [Fact]
    public void UnusablePictures_IsNotEmpty() => UnusablePictures.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(UnusablePictures))]
    public async Task Handle_ProfileScope_UnusablePicture_IsOmitted(string? composed)
    {
        _images.Setup(c => c.Compose("stored-key")).Returns(composed);

        var body = await BodyAsync(CreateUser(picture: "stored-key"), "openid profile");

        body.TryGetProperty("picture", out _).Should().BeFalse();
        body.GetProperty("name").GetString().Should().Be("Test User", "only the picture is dropped");
    }

    // --- A subject that can no longer be served (R1b) ---

    [Fact]
    public async Task Handle_UserMissingOrDeleted_ReturnsNotFound()
    {
        var subject = CreateUser();

        var answer = await AskAsync(stored: null, subject, "openid profile email phone");

        answer.IsError.Should().BeTrue();
        answer.Code.Should().Be(UserErrors.NotFound(subject.Id).Code);
    }

    public static TheoryData<UserStatus, int?> UnservableAccounts => new()
    {
        { UserStatus.Inactive, null },
        { UserStatus.Pending, null },
        // A live lockout: renewal refuses it, so does userinfo.
        { UserStatus.Locked, 30 },
    };

    [Fact]
    public void UnservableAccounts_IsNotEmpty() => UnservableAccounts.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(UnservableAccounts))]
    public async Task Handle_UserWhoCannotRenewCredentials_ReturnsAccountLocked(UserStatus status, int? lockedForMinutes)
    {
        var user = CreateUser(
            status: status,
            lockoutEnd: lockedForMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);
        user.CanRenewCredentials().Should().BeFalse("the case must be one renewal refuses");

        var answer = await AskAsync(user, user, "openid profile email phone");

        answer.IsError.Should().BeTrue();
        answer.Code.Should().Be(UserErrors.AccountLocked.Code);
    }

    [Fact]
    public async Task Handle_ReadsTheRowOnceByTheTokensSubject()
    {
        var user = CreateUser();

        await BodyAsync(user, "openid");

        _users.Verify(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
    }
}
