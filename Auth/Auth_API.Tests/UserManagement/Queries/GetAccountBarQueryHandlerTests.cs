using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.GetAccountBar;
using Auth.Application.Features.Users.GetMyApplications;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.UserManagement.Queries;

/// <summary>
/// The account bar of a linked application (owner decisions of 2026-11-02): the person the
/// single sign-on cookie names, and their tiles by the rule IAM's Home uses.
/// </summary>
public class GetAccountBarQueryHandlerTests
{
    private const string Cookie = "sso-cookie";

    private readonly Mock<IIdpSessionReader> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IApplicationAccessRepository> _accessRepository = new();
    private readonly Mock<IImageUrlComposer> _images = new();

    public GetAccountBarQueryHandlerTests()
    {
        _images
            .Setup(c => c.Compose(It.IsAny<string?>()))
            .Returns((string? key) => key is null ? null : $"https://cdn.example.com/{key}");
    }

    private GetAccountBarQueryHandler Handler(params string[] firstPartyOrigins) => new(
        _sessions.Object,
        _users.Object,
        new MyApplicationsList(
            _accessRepository.Object,
            new ApplicationLaunchTarget(TestHelpers.CreateOptions(new JwtSettings { Issuer = "https://id.example.com" })),
            _images.Object,
            TestHelpers.CreateOptions(new IdentityProviderSettings { FirstPartySpaOrigins = firstPartyOrigins })),
        _images.Object);

    private static User CreateUser(UserStatus status = UserStatus.Active) => new(
        id: Guid.NewGuid(),
        email: "maya@example.com",
        normalizedEmail: "MAYA@EXAMPLE.COM",
        passwordHash: "hash",
        firstName: "Maya",
        lastName: "Haddad",
        displayName: null,
        phoneNumber: null,
        status: status,
        emailConfirmed: true,
        phoneConfirmed: false,
        twoFactorEnabled: false,
        failedLoginAttempts: 0,
        lockoutEnd: null,
        lastLoginAt: null,
        passwordChangedAt: DateTime.UtcNow,
        mustChangePassword: false,
        preferredLanguage: "ar",
        timeZone: null,
        metadata: null,
        isSystemUser: false,
        createdAt: DateTime.UtcNow,
        createdBy: Guid.Empty,
        modifiedAt: null,
        modifiedBy: null,
        profileImageUrl: "avatars/maya.webp");

    private void SessionFor(User user) =>
        _sessions
            .Setup(r => r.FindLiveAsync(Cookie, IdpSessionReadMode.Inspect, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdpSession(
                Guid.NewGuid(), user.Id, "hash", DateTime.UtcNow, DateTime.UtcNow.AddHours(1),
                revokedAt: null, ipAddress: null, deviceInfo: null));

    private static UserApplicationAccess Access(string name, string? baseUrl) =>
        new(Guid.NewGuid(), name.ToUpperInvariant(), name, "logos/app.webp", IsActive: true,
            ViaOpenAccess: true, ViaGrant: false, LogoUrlDark: null, baseUrl, InitiateLoginUri: null,
            HasBrowserSignIn: true);

    [Fact]
    public async Task NoLiveSession_AsksToSignIn_AndReadsNothingElse()
    {
        var result = await Handler().Handle(new GetAccountBarQuery(Cookie), CancellationToken.None);

        result.Value.Should().Be(AccountBarResult.SignIn);
        _users.VerifyNoOtherCalls();
        _accessRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TheSessionIsOnlyInspected_SoDrawingTheBarNeverKeepsAnIdleSessionAlive()
    {
        await Handler().Handle(new GetAccountBarQuery(Cookie), CancellationToken.None);

        _sessions.Verify(
            r => r.FindLiveAsync(Cookie, IdpSessionReadMode.Inspect, It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(
            r => r.FindLiveAsync(It.IsAny<string?>(), It.Is<IdpSessionReadMode>(m => m != IdpSessionReadMode.Inspect),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _sessions.Verify(r => r.RecordUseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Pending)]
    public async Task AnAccountThatMayNoLongerSignIn_IsNotShownAsSignedIn(UserStatus status)
    {
        var user = CreateUser(status);
        SessionFor(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await Handler().Handle(new GetAccountBarQuery(Cookie), CancellationToken.None);

        result.Value.Should().Be(AccountBarResult.SignIn);
        _accessRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ASignedInPerson_GetsTheirIdentity_AndTheTilesHomeShows()
    {
        var user = CreateUser();
        SessionFor(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _accessRepository
            .Setup(r => r.GetApplicationsForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Access("Crm", "https://crm.example.com/"),
                // One of the platform's own apps registered as an application: Home leaves it out.
                Access("Console", "https://console.example.com/"),
                // No safe way in: Home leaves it out too.
                Access("Legacy", "http://legacy.example.com/")
            ]);

        var result = await Handler("https://console.example.com").Handle(
            new GetAccountBarQuery(Cookie), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.SignInRequired.Should().BeFalse();
        var account = result.Value.Account!;
        account.Name.Should().Be("Maya Haddad");
        account.Email.Should().Be("maya@example.com");
        account.ProfileImageUrl.Should().Be("https://cdn.example.com/avatars/maya.webp");
        account.PreferredLanguage.Should().Be("ar");
        account.Applications.Select(a => a.Name).Should().Equal("Crm");
        account.Applications[0].LaunchUrl.Should().Be("https://crm.example.com/");
    }
}
