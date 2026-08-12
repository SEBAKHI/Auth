using Auth.Application.Features.Authentication.GetLoginHistory;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Authentication;
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

    private void GivenAttempts(params SignInHistoryEntry[] attempts) =>
        _attemptsMock
            .Setup(r => r.GetSignInHistoryAsync(_userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempts);

    private static SignInHistoryEntry Attempt(
        bool success = true,
        string? failureReason = null,
        string? userAgent = ChromeOnWindows,
        Guid? challengeId = null,
        int secondFactorAttempts = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            AttemptedAt = DateTime.UtcNow,
            IsSuccess = success,
            FailureReason = failureReason,
            IpAddress = "203.0.113.10",
            UserAgent = userAgent,
            TwoFactorChallengeId = challengeId,
            SecondFactorAttempts = secondFactorAttempts
        };

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
    public async Task ResolvesTheLocationFromTheAddress()
    {
        // Always resolved on read. The table has no location column at all, so
        // there has never been a stored value to prefer -- the read model does not
        // carry one, which is why the old "prefers the stored location" case is
        // gone rather than merely failing.
        GivenAttempts(Attempt());
        _geoMock.Setup(g => g.Resolve("203.0.113.10")).Returns("Istanbul, Türkiye");

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.Location.Should().Be("Istanbul, Türkiye");
    }

    [Fact]
    public async Task AnUnfinishedCeremonyIsReportedAsNeitherSuccessNorFailure()
    {
        // The row a sign-in leaves when the password was accepted and the code
        // never arrived. Reporting it as a failure is the defect this replaced.
        GivenAttempts(Attempt(
            success: false, failureReason: null, challengeId: Guid.NewGuid(), secondFactorAttempts: 3));

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.IsSuccess.Should().BeFalse();
        entry.SecondFactorIncomplete.Should().BeTrue();
        entry.SecondFactorAttempts.Should().Be(3);
        entry.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ACompletedTwoFactorSignInIsNotFlaggedIncomplete()
    {
        GivenAttempts(Attempt(success: true, challengeId: Guid.NewGuid(), secondFactorAttempts: 2));

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.IsSuccess.Should().BeTrue();
        entry.SecondFactorIncomplete.Should().BeFalse();
        entry.SecondFactorAttempts.Should().Be(2);
    }

    [Fact]
    public async Task ASettledTwoFactorFailureIsAFailureNotAnUnfinishedCeremony()
    {
        GivenAttempts(Attempt(
            success: false,
            failureReason: "Too many incorrect verification codes",
            challengeId: Guid.NewGuid(),
            secondFactorAttempts: 5));

        var entry = (await _handler.Handle(
            new GetLoginHistoryQuery(_userId), CancellationToken.None)).Value.Single();

        entry.SecondFactorIncomplete.Should().BeFalse();
        entry.FailureReason.Should().Be("Too many incorrect verification codes");
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
