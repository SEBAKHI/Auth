using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
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
            new Mock<IGeoIpLookup>().Object,
            new Mock<ICredentialRevocationService>().Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                Issuer = "test",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TestHelpers.CreateOptions(notifications ?? new NotificationSettings()),
            // Default settings mean MaxConcurrentSessions = 0, so the session
            // limit stays out of the way of what these tests are about.
            TestHelpers.CreateOptions(new SessionSettings()),
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    private Task Sign(
        string? userAgent = ChromeOnWindows,
        NotificationSettings? notifications = null,
        string? deviceId = null) =>
        CreateBuilder(notifications).BuildAsync(
            _user, "203.0.113.10", userAgent, deviceId, CancellationToken.None,
            establishIdpSession: false);

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
            _user, "203.0.113.10", ChromeOnWindows, deviceId: null, CancellationToken.None,
            establishIdpSession: false);

        response.IsError.Should().BeFalse();
        response.Value.Token!.AccessToken.Should().Be("access-token");
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

    [Fact]
    public async Task SessionCarriesTheSameSignatureTheLedgerWasKeyedOn()
    {
        // The whole point of the linkage: if these two ever derive the signature
        // separately they can drift, and a session could never be attributed to
        // the browser that started it. Captured from both sides and compared.
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        UserKnownDevice? ledgerRow = null;
        _devicesMock
            .Setup(r => r.UpsertAsync(It.IsAny<UserKnownDevice>(), It.IsAny<CancellationToken>()))
            .Callback<UserKnownDevice, CancellationToken>((d, _) => ledgerRow = d)
            .ReturnsAsync(true);

        UserSession? sessionRow = null;
        _sessionsMock
            .Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback<UserSession, CancellationToken>((s, _) => sessionRow = s)
            .ReturnsAsync((UserSession s, CancellationToken _) => s);

        await Sign(deviceId: "device-abc");

        sessionRow.Should().NotBeNull();
        ledgerRow.Should().NotBeNull();
        sessionRow!.DeviceHash.Should().Be(ledgerRow!.DeviceHash);
        sessionRow.DeviceName.Should().Be("Chrome on Windows");
        sessionRow.DeviceId.Should().Be("device-abc");
        sessionRow.DeviceType.Should().Be(DeviceType.Desktop);
    }

    [Fact]
    public async Task DifferentBrowsersOnOneMachineAreDifferentSignatures()
    {
        // The signature covers the browser family as well as the client id, so
        // "device" here means a browser profile — which is why the UI must not
        // promise the row is a machine.
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        var hashes = new List<string>();
        _devicesMock
            .Setup(r => r.UpsertAsync(It.IsAny<UserKnownDevice>(), It.IsAny<CancellationToken>()))
            .Callback<UserKnownDevice, CancellationToken>((d, _) => hashes.Add(d.DeviceHash))
            .ReturnsAsync(true);

        await Sign(ChromeOnWindows, deviceId: "same-id");
        await Sign("Mozilla/5.0 (Windows NT 10.0; rv:121.0) Gecko/20100101 Firefox/121.0",
            deviceId: "same-id");

        hashes.Should().HaveCount(2);
        hashes[0].Should().NotBe(hashes[1]);
    }

    [Fact]
    public async Task AUserAgentLongerThanItsColumnStillProducesASession()
    {
        // The combined "{ua} | DeviceId: {id}" string used to overflow
        // UserAgent's 500 characters; the insert threw, the guard swallowed it,
        // and the user was signed in with nothing to manage. The two halves are
        // separate columns now, and over-long input is truncated rather than
        // rejected. Truncation itself is the repository's job — this asserts the
        // builder hands the row over at all.
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        UserSession? sessionRow = null;
        _sessionsMock
            .Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback<UserSession, CancellationToken>((s, _) => sessionRow = s)
            .ReturnsAsync((UserSession s, CancellationToken _) => s);

        await Sign(ChromeOnWindows + new string('x', 600), deviceId: new string('d', 100));

        sessionRow.Should().NotBeNull();
        sessionRow!.DeviceHash.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Reconciling a browser first recorded before the client sent its
    /// identifier on every request.
    ///
    /// The reported defect: registering, then signing out and back in from the
    /// same browser, produced a second browser row and an email announcing it.
    /// The sign-in that completes registration went through verify-email, which
    /// carried no device id, so its signature was built from an empty one; the
    /// next real login sent an id and hashed to something else entirely.
    /// </summary>
    private void GivenLegacyRowIsAdopted() =>
        _devicesMock
            .Setup(r => r.AdoptLegacySignatureAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    [Fact]
    public async Task ABrowserHeldUnderItsPreHeaderSignature_IsAdoptedWithoutAnAlert()
    {
        GivenDeviceIsUnknown(userHasOtherDevices: true);
        GivenLegacyRowIsAdopted();

        await Sign(deviceId: "device-abc");

        VerifyAlerted(Times.Never());
    }

    [Fact]
    public async Task AdoptionProbesTheEmptyIdSignatureForTheSameBrowserAndOs()
    {
        // The legacy value is what ComputeHash produced with no device id but
        // the same browser and OS. Probing anything else would either miss the
        // row or, worse, adopt a different browser's.
        GivenDeviceIsUnknown(userHasOtherDevices: true);
        GivenLegacyRowIsAdopted();

        var expected = UserKnownDevice.ComputeHash(null, "Chrome", "Windows");
        var current = UserKnownDevice.ComputeHash("device-abc", "Chrome", "Windows");

        await Sign(deviceId: "device-abc");

        _devicesMock.Verify(
            r => r.AdoptLegacySignatureAsync(
                _user.Id, expected, current, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WithNoDeviceIdThereIsNothingToReconcile()
    {
        // Without an id the current signature already IS the legacy one, so a
        // probe would ask whether the row it is about to create already exists.
        GivenDeviceIsUnknown(userHasOtherDevices: true);

        await Sign(deviceId: null);

        _devicesMock.Verify(
            r => r.AdoptLegacySignatureAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyAlerted(Times.Once());
    }

    [Fact]
    public async Task NothingToAdopt_StillTreatsTheBrowserAsNewAndAlerts()
    {
        // The probe must not swallow the genuine case it sits in front of.
        GivenDeviceIsUnknown(userHasOtherDevices: true);
        _devicesMock
            .Setup(r => r.AdoptLegacySignatureAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Sign(deviceId: "device-abc");

        VerifyAlerted(Times.Once());
    }

    [Fact]
    public async Task ARecognisedBrowserNeverPaysForTheAdoptionProbe()
    {
        // Adoption is a one-time reconciliation on the miss path. Every ordinary
        // sign-in must stay at a single lookup.
        var known = UserKnownDevice.Create(_user.Id, "hash", "Chrome on Windows");
        _devicesMock
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(known);

        await Sign(deviceId: "device-abc");

        _devicesMock.Verify(
            r => r.AdoptLegacySignatureAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
