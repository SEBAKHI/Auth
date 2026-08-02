using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Covers the decision the new-device alert turns on, exercised through
/// <see cref="LoginResponseBuilder"/> because that is the single point every
/// successful sign-in passes through.
/// </summary>
public class NewDeviceAlertTests
{
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

    private readonly Mock<IUserKnownDeviceRepository> _devicesMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<IUserSessionRepository> _sessionsMock = new();
    private readonly User _user = TestHelpers.CreateUser(email: "user@example.com");

    /// <summary>Builds the subject with every collaborator stubbed to succeed.</summary>
    private LoginResponseBuilder CreateBuilder(NotificationSettings? notifications = null)
    {
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var organizations = new Mock<IOrganizationRepository>();
        organizations.Setup(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid>(), It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>()))
            .Returns("access-token");
        jwt.Setup(s => s.GenerateRefreshToken()).Returns("refresh-token");
        jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns(Guid.NewGuid().ToString());

        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("hash");

        return new LoginResponseBuilder(
            roles.Object,
            permissions.Object,
            organizations.Object,
            jwt.Object,
            keys.Object,
            new Mock<IRefreshTokenRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<ILoginAttemptRepository>().Object,
            _sessionsMock.Object,
            new Mock<IIdpSessionRepository>().Object,
            _devicesMock.Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                Issuer = "test",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TestHelpers.CreateOptions(notifications ?? new NotificationSettings()),
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    private Task Sign(string? deviceInfo = ChromeOnWindows, NotificationSettings? notifications = null) =>
        CreateBuilder(notifications).BuildAsync(
            _user, "203.0.113.10", deviceInfo, CancellationToken.None, establishIdpSession: false);

    private void GivenDeviceIsUnknown(bool userHasOtherDevices, bool insertWins = true)
    {
        _devicesMock
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserKnownDevice?)null);
        _devicesMock
            .Setup(r => r.HasAnyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userHasOtherDevices);
        _devicesMock
            .Setup(r => r.UpsertAsync(It.IsAny<UserKnownDevice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(insertWins);
    }

    private void VerifyAlerted(Times times) =>
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<NewDeviceSignInEvent>(), It.IsAny<CancellationToken>()),
            times);

    [Fact]
    public async Task FirstDeviceEver_DoesNotAlert()
    {
        // Alerting here would report the sign-in the user is performing right
        // now, moments after registering.
        GivenDeviceIsUnknown(userHasOtherDevices: false);

        await Sign();

        VerifyAlerted(Times.Never());
    }

    [Fact]
    public async Task NewDeviceOnAnEstablishedAccount_Alerts()
    {
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        await Sign();

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<NewDeviceSignInEvent>(e =>
                    e.UserId == _user.Id &&
                    e.DeviceName == "Chrome on Windows" &&
                    e.IpAddress == "203.0.113.10"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecognisedDevice_DoesNotAlertAndRefreshesTheSighting()
    {
        var known = UserKnownDevice.Create(_user.Id, "hash", "Chrome on Windows");
        _devicesMock
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(known);

        await Sign();

        VerifyAlerted(Times.Never());
        _devicesMock.Verify(
            r => r.UpsertAsync(known, It.IsAny<CancellationToken>()), Times.Once);
        // A recognised device must not cost the extra probes.
        _devicesMock.Verify(
            r => r.HasAnyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithinTheAlertFloor_RecordsTheDeviceButStaysQuiet()
    {
        // Someone clearing site data every session presents a new signature
        // each time; the floor is what stops that becoming an email per login.
        GivenDeviceIsUnknown(userHasOtherDevices: true);
        _devicesMock
            .Setup(r => r.GetLastAlertAtAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5));

        await Sign(notifications: new NotificationSettings { NewDeviceAlertMinIntervalMinutes = 60 });

        VerifyAlerted(Times.Never());
        _devicesMock.Verify(
            r => r.UpsertAsync(It.IsAny<UserKnownDevice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PastTheAlertFloor_Alerts()
    {
        GivenDeviceIsUnknown(userHasOtherDevices: true);
        _devicesMock
            .Setup(r => r.GetLastAlertAtAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-90));

        await Sign(notifications: new NotificationSettings { NewDeviceAlertMinIntervalMinutes = 60 });

        VerifyAlerted(Times.Once());
    }

    [Fact]
    public async Task LosingTheInsertRace_DoesNotAlertTwice()
    {
        // Two concurrent sign-ins from the same new device both see it as
        // unknown; only the one that created the row is the discovery.
        GivenDeviceIsUnknown(userHasOtherDevices: true, insertWins: false);

        await Sign();

        VerifyAlerted(Times.Never());
    }

    [Fact]
    public async Task Disabled_SkipsDeviceTrackingEntirely()
    {
        await Sign(notifications: new NotificationSettings { NewDeviceAlertEnabled = false });

        VerifyAlerted(Times.Never());
        _devicesMock.Verify(
            r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeviceRepositoryFailure_StillCompletesTheSignIn()
    {
        // Device tracking sits inside the session-tracking guard precisely so a
        // notification problem can never cost the user their login.
        _devicesMock
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var response = await CreateBuilder().BuildAsync(
            _user, "203.0.113.10", ChromeOnWindows, CancellationToken.None, establishIdpSession: false);

        response.Token!.AccessToken.Should().Be("access-token");
        VerifyAlerted(Times.Never());
    }

    [Fact]
    public async Task UnparseableUserAgent_StillTracksButNamesNothing()
    {
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        await Sign("curl/8.4.0");

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<NewDeviceSignInEvent>(e => e.DeviceName == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
