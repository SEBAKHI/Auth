using Auth.Application.Features.Authentication.GetKnownDevices;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// The device list is a join between the recognition ledger and the live
/// sessions, keyed on the signature both sides now carry.
/// </summary>
public class GetKnownDevicesQueryHandlerTests
{
    private const string ChromeHash = "hash-chrome";
    private const string FirefoxHash = "hash-firefox";

    private readonly Mock<IUserKnownDeviceRepository> _devicesMock = new();
    private readonly Mock<IUserSessionRepository> _sessionsMock = new();
    private readonly GetKnownDevicesQueryHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public GetKnownDevicesQueryHandlerTests()
    {
        _handler = new GetKnownDevicesQueryHandler(
            _devicesMock.Object,
            _sessionsMock.Object,
            new Mock<ILogger<GetKnownDevicesQueryHandler>>().Object);

        _sessionsMock
            .Setup(r => r.GetActiveSessionsForUserAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void GivenDevices(params UserKnownDevice[] devices) =>
        _devicesMock
            .Setup(r => r.GetForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(devices);

    private void GivenSessions(params UserSession[] sessions) =>
        _sessionsMock
            .Setup(r => r.GetActiveSessionsForUserAsync(
                _userId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

    private static UserKnownDevice Device(string hash, string name) =>
        new(Guid.NewGuid(), Guid.Empty, hash, name, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow, null);

    [Fact]
    public async Task NoDevices_ReturnsEmptyWithoutQueryingSessions()
    {
        GivenDevices();

        var result = await _handler.Handle(new GetKnownDevicesQuery(_userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
        _sessionsMock.Verify(
            r => r.GetActiveSessionsForUserAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CountsOnlyTheSessionsBelongingToEachBrowser()
    {
        // The same physical machine running two browsers is two rows, because the
        // signature covers the browser family. This is the case the UI copy has
        // to be honest about.
        GivenDevices(Device(ChromeHash, "Chrome on Windows"), Device(FirefoxHash, "Firefox on Windows"));
        GivenSessions(
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: ChromeHash),
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: ChromeHash),
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: FirefoxHash));

        var result = await _handler.Handle(new GetKnownDevicesQuery(_userId), CancellationToken.None);

        result.Value.Single(d => d.DeviceName == "Chrome on Windows").ActiveSessionCount.Should().Be(2);
        result.Value.Single(d => d.DeviceName == "Firefox on Windows").ActiveSessionCount.Should().Be(1);
    }

    [Fact]
    public async Task ADeviceWithNoLiveSessionsStillAppears()
    {
        // The ledger outlives the credential: a browser the user signed out of is
        // still one they have used, and is exactly what they may want to forget.
        GivenDevices(Device(ChromeHash, "Chrome on Windows"));

        var result = await _handler.Handle(new GetKnownDevicesQuery(_userId), CancellationToken.None);

        var device = result.Value.Single();
        device.ActiveSessionCount.Should().Be(0);
        device.IsCurrent.Should().BeFalse();
        device.DeviceType.Should().Be(DeviceType.Unknown);
    }

    [Fact]
    public async Task MarksTheBrowserHoldingTheCallersOwnSession()
    {
        var currentSessionId = Guid.NewGuid();
        GivenDevices(Device(ChromeHash, "Chrome on Windows"), Device(FirefoxHash, "Firefox on Windows"));
        GivenSessions(
            TestHelpers.CreateUserSession(id: currentSessionId, userId: _userId, deviceHash: ChromeHash),
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: FirefoxHash));

        var result = await _handler.Handle(
            new GetKnownDevicesQuery(_userId, currentSessionId), CancellationToken.None);

        result.Value.Single(d => d.DeviceName == "Chrome on Windows").IsCurrent.Should().BeTrue();
        result.Value.Single(d => d.DeviceName == "Firefox on Windows").IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task TakesTheFormFactorFromTheMostRecentSession()
    {
        // The ledger has no form-factor column — it predates one, and the form
        // factor belongs to the sign-in rather than to the signature.
        var older = TestHelpers.CreateUserSession(
            userId: _userId, deviceHash: ChromeHash, deviceType: DeviceType.Desktop,
            lastActivityAt: DateTime.UtcNow.AddHours(-5));
        var newer = TestHelpers.CreateUserSession(
            userId: _userId, deviceHash: ChromeHash, deviceType: DeviceType.Mobile,
            lastActivityAt: DateTime.UtcNow);

        GivenDevices(Device(ChromeHash, "Chrome on Android"));
        GivenSessions(older, newer);

        var result = await _handler.Handle(new GetKnownDevicesQuery(_userId), CancellationToken.None);

        result.Value.Single().DeviceType.Should().Be(DeviceType.Mobile);
    }

    [Fact]
    public async Task SessionsWithNoSignatureAreNotAttributedToAnyBrowser()
    {
        // An OAuth token exchange sends no client identifier, so its session
        // cannot belong to a browser row. It must not be counted under one.
        GivenDevices(Device(ChromeHash, "Chrome on Windows"));
        GivenSessions(
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: ChromeHash),
            TestHelpers.CreateUserSession(userId: _userId, deviceHash: null));

        var result = await _handler.Handle(new GetKnownDevicesQuery(_userId), CancellationToken.None);

        result.Value.Single().ActiveSessionCount.Should().Be(1);
    }
}
