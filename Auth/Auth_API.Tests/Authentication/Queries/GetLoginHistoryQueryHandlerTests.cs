using Auth.Application.Features.Authentication.GetLoginHistory;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// The read-only companion to the session list: what has been tried against the
/// account, as opposed to what is signed in right now.
/// </summary>
public class GetLoginHistoryQueryHandlerTests
{
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

    private readonly Mock<ILoginAttemptRepository> _attemptsMock = new();
    private readonly Mock<IGeoIpLookup> _geoMock = new();
    private readonly GetLoginHistoryQueryHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public GetLoginHistoryQueryHandlerTests()
    {
        _handler = new GetLoginHistoryQueryHandler(
            _attemptsMock.Object,
            _geoMock.Object,
            new Mock<ILogger<GetLoginHistoryQueryHandler>>().Object);
    }

    private void GivenAttempts(params LoginAttempt[] attempts) =>
        _attemptsMock
            .Setup(r => r.GetRecentByUserAsync(_userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempts);

    private static LoginAttempt Attempt(
        bool success = true,
        string? failureReason = null,
        string? userAgent = ChromeOnWindows,
        string? location = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user@example.com", success, failureReason,
            "203.0.113.10", userAgent, location, DateTime.UtcNow, null);

    [Fact]
    public async Task LabelsTheClientFromTheStoredUserAgent()
    {
        // Parsed on read, not stored: these rows long predate the device columns,
        // and the raw agent is not something to put in front of a user.
        GivenAttempts(Attempt());

        var result = await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None);

        var entry = result.Value.Single();
        entry.DeviceName.Should().Be("Chrome on Windows");
        entry.DeviceType.Should().Be(DeviceType.Desktop);
    }

    [Fact]
    public async Task CarriesFailuresAndTheirReasonThrough()
    {
        // The reason is passed through as stored. The values written do not match
        // the vocabulary the table's own comment documents, so mapping them to an
        // enum would silently drop whatever did not fit.
        GivenAttempts(Attempt(success: false, failureReason: "Invalid password"));

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.IsSuccess.Should().BeFalse();
        entry.FailureReason.Should().Be("Invalid password");
    }

    [Fact]
    public async Task ResolvesTheLocationWhenTheRowDoesNotCarryOne()
    {
        // No handler has ever written this column, so reading through to the
        // lookup is what gives the existing history any locations at all.
        GivenAttempts(Attempt(location: null));
        _geoMock.Setup(g => g.Resolve("203.0.113.10")).Returns("Istanbul, Türkiye");

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.Location.Should().Be("Istanbul, Türkiye");
    }

    [Fact]
    public async Task PrefersAStoredLocationOverAFreshLookup()
    {
        GivenAttempts(Attempt(location: "Recorded at the time"));
        _geoMock.Setup(g => g.Resolve(It.IsAny<string?>())).Returns("Resolved now");

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.Location.Should().Be("Recorded at the time");
    }

    [Fact]
    public async Task AnUnparseableAgentNamesNothingRatherThanGuessing()
    {
        GivenAttempts(Attempt(userAgent: "curl/8.4.0"));

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.DeviceName.Should().BeNull();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void TakeIsBoundedTheSameWayEveryOtherListEndpointIs(int take, bool valid)
    {
        var result = new GetLoginHistoryQueryValidator()
            .Validate(new GetLoginHistoryQuery(_userId, take));

        result.IsValid.Should().Be(valid);
    }
}
