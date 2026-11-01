using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.GetMyApplications;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.UserManagement.Queries;

/// <summary>
/// UI batch item 12: the accounts app's Home lists the applications the user can open, one
/// tile each. The handler decides nothing itself: entitlement is the sign-in gate's list, the
/// address is <see cref="ApplicationLaunchTarget"/>'s, and an application with no way in, or
/// one of the platform's own browser apps, is left out (owner decision D9).
/// </summary>
public class GetMyApplicationsQueryHandlerTests
{
    private const string Issuer = "https://id.example.com/auth";
    private const string EscapedIssuer = "https%3A%2F%2Fid.example.com%2Fauth";

    private readonly Mock<IApplicationAccessRepository> _accessRepository = new();
    private readonly Mock<IImageUrlComposer> _imageUrlComposer = new();

    public GetMyApplicationsQueryHandlerTests()
    {
        _imageUrlComposer
            .Setup(c => c.Compose(It.IsAny<string?>()))
            .Returns((string? key) => key is null ? null : $"https://cdn.example.com/{key}");
    }

    private GetMyApplicationsQueryHandler Handler(params string[] firstPartyOrigins) => new(
        _accessRepository.Object,
        new ApplicationLaunchTarget(TestHelpers.CreateOptions(new JwtSettings { Issuer = Issuer })),
        _imageUrlComposer.Object,
        TestHelpers.CreateOptions(new IdentityProviderSettings { FirstPartySpaOrigins = firstPartyOrigins }));

    private static UserApplicationAccess Access(
        string name,
        string? baseUrl = null,
        string? initiateLoginUri = null,
        bool hasBrowserSignIn = true,
        string? logoUrl = null,
        string? logoUrlDark = null,
        bool isActive = true) =>
        new(Guid.NewGuid(), name.ToUpperInvariant(), name, logoUrl, isActive,
            ViaOpenAccess: true, ViaGrant: false, logoUrlDark, baseUrl, initiateLoginUri, hasBrowserSignIn);

    private void Returns(Guid userId, params UserApplicationAccess[] rows) =>
        _accessRepository
            .Setup(r => r.GetApplicationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Handle_AsksTheSignInGateForTheCallerAlone()
    {
        var userId = Guid.NewGuid();
        Returns(userId);

        await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        _accessRepository.Verify(
            r => r.GetApplicationsForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _accessRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_MapsEachTileWithItsLaunchAddressAndComposedLogos()
    {
        var userId = Guid.NewGuid();
        var crm = Access("Crm", initiateLoginUri: "https://crm.example.com/signin/start",
            logoUrl: "logos/crm.webp", logoUrlDark: "logos/crm-dark.webp");
        var wiki = Access("Wiki", baseUrl: "https://wiki.example.com/home");
        Returns(userId, crm, wiki);

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);

        var crmTile = result.Value[0];
        crmTile.Id.Should().Be(crm.ApplicationId);
        crmTile.Name.Should().Be("Crm");
        crmTile.LaunchUrl.Should().Be($"https://crm.example.com/signin/start?iss={EscapedIssuer}",
            "the initiate-login URI wins and carries the issuer (OpenID Connect Core 1.0 §4)");
        crmTile.LogoUrl.Should().Be("https://cdn.example.com/logos/crm.webp");
        crmTile.LogoUrlDark.Should().Be("https://cdn.example.com/logos/crm-dark.webp");

        result.Value[1].LaunchUrl.Should().Be("https://wiki.example.com/home",
            "without an initiate-login URI the home URL is the way in");
        result.Value[1].LogoUrlDark.Should().BeNull();
    }

    [Fact]
    public async Task Handle_KeepsTheRepositoryOrder()
    {
        var userId = Guid.NewGuid();
        Returns(userId,
            Access("Alpha", baseUrl: "https://a.example.com"),
            Access("Bravo", baseUrl: "https://b.example.com"),
            Access("Charlie", baseUrl: "https://c.example.com"));

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Select(a => a.Name).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Theory]
    [InlineData(null, null, true, "no address registered")]
    [InlineData(null, "https://api.example.com", false, "a machine client never exposes its address")]
    [InlineData(null, "http://crm.example.com", true, "plain http off the loopback host is not a destination")]
    [InlineData(null, "javascript:alert(1)", true, "not a web address")]
    [InlineData("javascript:alert(1)", null, false, "a stored initiate-login URI the save rule refuses")]
    [InlineData("https://crm.example.com@evil.example.com/login", null, false, "user information reads as another host")]
    [InlineData("http://crm.example.com/login", null, false, "plain http initiate-login URI")]
    [InlineData(null, "http://localhost:3000/", true, "a development loopback address is never a tile (D9: https)")]
    [InlineData("http://localhost:3000/signin", null, false, "nor a development initiate-login URI")]
    [InlineData(null, "https://crm.example.com@evil.example.com/", true, "user information in the home URL")]
    public async Task Handle_LeavesOutAnApplicationWithNoSafeWayIn(
        string? initiateLoginUri, string? baseUrl, bool hasBrowserSignIn, string because)
    {
        var userId = Guid.NewGuid();
        Returns(userId,
            Access("Hidden", baseUrl, initiateLoginUri, hasBrowserSignIn),
            Access("Shown", baseUrl: "https://shown.example.com"));

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Select(a => a.Name).Should().Equal(["Shown"], because);
    }

    [Fact]
    public async Task Handle_LeavesOutAnApplicationTheRowSaysIsSwitchedOff()
    {
        // The sign-in gate's list holds active applications only; the tile rule checks again
        // rather than trusting that a shared query never changes.
        var userId = Guid.NewGuid();
        Returns(userId,
            Access("Off", baseUrl: "https://off.example.com", isActive: false),
            Access("On", baseUrl: "https://on.example.com"));

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Select(a => a.Name).Should().Equal("On");
    }

    [Fact]
    public async Task Handle_KeepsAHashRoutedHomeUrl()
    {
        var userId = Guid.NewGuid();
        Returns(userId, Access("Spa", baseUrl: "https://spa.example.com/#/"));

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Should().ContainSingle().Which.LaunchUrl.Should().Be("https://spa.example.com/#/");
    }

    [Theory]
    [InlineData("https://accounts.example.com", "https://accounts.example.com/")]
    [InlineData("https://Accounts.Example.com/", "https://accounts.example.com/profile")]
    [InlineData("https://console.example.com:443", "https://console.example.com/")]
    public async Task Handle_LeavesOutThePlatformsOwnBrowserApps(string configuredOrigin, string launch)
    {
        var userId = Guid.NewGuid();
        Returns(userId,
            Access("Own", baseUrl: launch),
            Access("Crm", baseUrl: "https://crm.example.com"));

        var result = await Handler(configuredOrigin, "https://other.example.com")
            .Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Select(a => a.Name).Should().Equal(["Crm"],
            "Home is one of the platform's own apps already (D9)");
    }

    [Fact]
    public async Task Handle_AnotherPortOfAFirstPartyHost_IsAnotherOrigin()
    {
        var userId = Guid.NewGuid();
        Returns(userId, Access("Crm", baseUrl: "https://accounts.example.com:8443/"));

        var result = await Handler("https://accounts.example.com")
            .Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoEntitledApplication_IsAnEmptyListNotAnError()
    {
        var userId = Guid.NewGuid();
        Returns(userId);

        var result = await Handler().Handle(new GetMyApplicationsQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }
}
