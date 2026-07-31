using Auth.Application.Features.Dashboard.GetAppActivityStats;
using Auth.Application.Features.Dashboard.GetAuditStats;
using Auth.Application.Features.Dashboard.GetAuthStats;
using Auth.Application.Features.Dashboard.GetSessionStats;
using Auth.Application.Features.Dashboard.GetUserStats;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Dashboard;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Dashboard;

public class GetUserStatsQueryHandlerTests
{
    private readonly Mock<IDashboardStatsRepository> _repoMock = new();
    private readonly GetUserStatsQueryHandler _handler;

    public GetUserStatsQueryHandlerTests()
    {
        _handler = new GetUserStatsQueryHandler(
            _repoMock.Object,
            new Mock<ILogger<GetUserStatsQueryHandler>>().Object);
    }

    private static UserStatsSnapshot CreateSnapshot() => new()
    {
        TotalUsers = 12,
        ByStatus = [new UserStatusCount(1, 9), new UserStatusCount(3, 3)],
        MfaEnabled = 4,
        ActiveUsers = 9,
        NewInWindow = 5,
        SignupsPerDay = [new DailyCount(new DateTime(2026, 6, 23), 3), new DailyCount(new DateTime(2026, 6, 24), 2)],
        CohortCreated = 5,
        CohortEmailConfirmed = 4,
        CohortLoggedIn = 3,
        DormantOver30Days = 2,
        DormantOver60Days = 1,
        DormantOver90Days = 0,
        NeverLoggedIn = 1,
        UsersByOrganization = [new OrganizationUserCount(Guid.NewGuid(), "Acme", false, 7)],
        TotalActiveMemberships = 8
    };

    [Fact]
    public async Task Handle_MapsSnapshotToDto()
    {
        _repoMock
            .Setup(r => r.GetUserStatsAsync(30, "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());

        var result = await _handler.Handle(new GetUserStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Days.Should().Be(30);
        result.Value.TotalUsers.Should().Be(12);
        result.Value.ByStatus.Should().HaveCount(2);
        result.Value.ByStatus[0].Status.Should().Be(1);
        result.Value.ByStatus[0].Count.Should().Be(9);
        result.Value.MfaEnabled.Should().Be(4);
        result.Value.ActiveUsers.Should().Be(9);
        result.Value.SignupsPerDay.Should().HaveCount(2);
        result.Value.SignupsPerDay[0].Date.Should().Be(new DateTime(2026, 6, 23));
        result.Value.CohortCreated.Should().Be(5);
        result.Value.CohortEmailConfirmed.Should().Be(4);
        result.Value.CohortLoggedIn.Should().Be(3);
        result.Value.UsersByOrganization.Should().ContainSingle()
            .Which.OrganizationName.Should().Be("Acme");
        result.Value.TotalActiveMemberships.Should().Be(8);
    }

    [Fact]
    public async Task Handle_PassesRequestedDaysToRepository()
    {
        _repoMock
            .Setup(r => r.GetUserStatsAsync(7, "Europe/Istanbul", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());

        var result = await _handler.Handle(
            new GetUserStatsQuery(7, "Europe/Istanbul"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Days.Should().Be(7);
        _repoMock.Verify(
            r => r.GetUserStatsAsync(7, "Europe/Istanbul", It.IsAny<CancellationToken>()),
            Times.Once());
    }
}

public class GetAuthStatsQueryHandlerTests
{
    private readonly Mock<IDashboardStatsRepository> _repoMock = new();
    private readonly GetAuthStatsQueryHandler _handler;

    public GetAuthStatsQueryHandlerTests()
    {
        _handler = new GetAuthStatsQueryHandler(
            _repoMock.Object,
            new Mock<ILogger<GetAuthStatsQueryHandler>>().Object);
    }

    private static AuthStatsSnapshot CreateSnapshot(Guid appId, Guid orgId) => new()
    {
        LoginsPerDay = [new DailyLoginCount(new DateTime(2026, 6, 23), 10, 2)],
        ActiveUsersPerDay = [new DailyCount(new DateTime(2026, 6, 23), 3)],
        ActiveUsersInWindow = 3,
        FailureReasons = [new ReasonCount("invalid_password", 2)],
        WindowSuccessCount = 10,
        WindowFailureCount = 2,
        PreviousWindowSuccessCount = 8,
        PreviousWindowFailureCount = 4,
        LockedOutNow = 1,
        LockoutEventsInWindow = 2,
        TopFailingIps = [new IpFailureCount("10.0.0.1", 5, 2)],
        LoginsByApplication = [new ApplicationLoginCount(appId, "Portal", 9, 1)],
        LoginsByOrganization =
        [
            new OrganizationLoginCount(orgId, "Acme", 6, 1),
            new OrganizationLoginCount(null, null, 4, 1)
        ]
    };

    [Fact]
    public async Task Handle_MapsSnapshotToDto()
    {
        var appId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetAuthStatsAsync(30, "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(appId, orgId));

        var result = await _handler.Handle(new GetAuthStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LoginsPerDay.Should().ContainSingle();
        result.Value.LoginsPerDay[0].SuccessCount.Should().Be(10);
        result.Value.LoginsPerDay[0].FailureCount.Should().Be(2);
        result.Value.ActiveUsersInWindow.Should().Be(3);
        result.Value.FailureReasons.Should().ContainSingle()
            .Which.Reason.Should().Be("invalid_password");
        result.Value.WindowSuccessCount.Should().Be(10);
        result.Value.PreviousWindowFailureCount.Should().Be(4);
        result.Value.LockedOutNow.Should().Be(1);
        result.Value.TopFailingIps.Should().ContainSingle()
            .Which.IpAddress.Should().Be("10.0.0.1");
        result.Value.LoginsByApplication.Should().ContainSingle()
            .Which.ApplicationId.Should().Be(appId);
    }

    [Fact]
    public async Task Handle_KeepsUnattributedOrganizationBucket()
    {
        _repoMock
            .Setup(r => r.GetAuthStatsAsync(30, "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(Guid.NewGuid(), Guid.NewGuid()));

        var result = await _handler.Handle(new GetAuthStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LoginsByOrganization.Should().HaveCount(2);
        var unattributed = result.Value.LoginsByOrganization.Single(o => o.OrganizationId is null);
        unattributed.SuccessCount.Should().Be(4);
        unattributed.FailureCount.Should().Be(1);
    }
}

public class GetSessionStatsQueryHandlerTests
{
    private readonly Mock<IDashboardStatsRepository> _repoMock = new();
    private readonly GetSessionStatsQueryHandler _handler;

    public GetSessionStatsQueryHandlerTests()
    {
        _handler = new GetSessionStatsQueryHandler(
            _repoMock.Object,
            new Mock<ILogger<GetSessionStatsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_MapsSnapshotToDto()
    {
        _repoMock
            .Setup(r => r.GetSessionStatsAsync(30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStatsSnapshot
            {
                ActiveSessions = 5,
                StaleOpenSessions = 2,
                StartedInWindow = 9,
                EndReasons = [new ReasonCount("logout", 4)],
                AverageSessionMinutes = 42.5,
                ActiveRefreshTokens = 11,
                TokensRevokedInWindow = 3,
                RevocationReasons = [new ReasonCount("rotated", 3)],
                TokensExpiringIn7Days = 6
            });

        var result = await _handler.Handle(new GetSessionStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ActiveSessions.Should().Be(5);
        result.Value.StaleOpenSessions.Should().Be(2);
        result.Value.StartedInWindow.Should().Be(9);
        result.Value.EndReasons.Should().ContainSingle().Which.Reason.Should().Be("logout");
        result.Value.AverageSessionMinutes.Should().Be(42.5);
        result.Value.ActiveRefreshTokens.Should().Be(11);
        result.Value.TokensExpiringIn7Days.Should().Be(6);
    }

    [Fact]
    public async Task Handle_NoEndedSessions_AverageIsNull()
    {
        _repoMock
            .Setup(r => r.GetSessionStatsAsync(30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStatsSnapshot
            {
                ActiveSessions = 0,
                StaleOpenSessions = 0,
                StartedInWindow = 0,
                EndReasons = [],
                AverageSessionMinutes = null,
                ActiveRefreshTokens = 0,
                TokensRevokedInWindow = 0,
                RevocationReasons = [],
                TokensExpiringIn7Days = 0
            });

        var result = await _handler.Handle(new GetSessionStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.AverageSessionMinutes.Should().BeNull();
        result.Value.EndReasons.Should().BeEmpty();
    }
}

public class GetAppActivityStatsQueryHandlerTests
{
    private readonly Mock<IDashboardStatsRepository> _repoMock = new();
    private readonly GetAppActivityStatsQueryHandler _handler;

    public GetAppActivityStatsQueryHandlerTests()
    {
        _handler = new GetAppActivityStatsQueryHandler(
            _repoMock.Object,
            new Mock<ILogger<GetAppActivityStatsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_MapsSnapshotToDto()
    {
        var appId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetAppActivityAsync(30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppActivitySnapshot
            {
                Applications =
                [
                    new ApplicationActivity(appId, "Portal", true, 20, 4, 3),
                    new ApplicationActivity(null, null, true, 2, 1, 0)
                ],
                OrganizationApplications =
                [
                    new OrganizationApplicationEnablement(orgId, "Acme", appId, "Portal", "pro", null)
                ]
            });

        var result = await _handler.Handle(new GetAppActivityStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Applications.Should().HaveCount(2);
        result.Value.Applications[0].ApplicationName.Should().Be("Portal");
        result.Value.Applications[0].SuccessfulLogins.Should().Be(20);
        result.Value.Applications[1].ApplicationId.Should().BeNull();
        result.Value.OrganizationApplications.Should().ContainSingle();
        result.Value.OrganizationApplications[0].SubscriptionTier.Should().Be("pro");
        result.Value.OrganizationApplications[0].OrganizationName.Should().Be("Acme");
    }
}

public class GetAuditStatsQueryHandlerTests
{
    private readonly Mock<IDashboardStatsRepository> _repoMock = new();
    private readonly GetAuditStatsQueryHandler _handler;

    public GetAuditStatsQueryHandlerTests()
    {
        _handler = new GetAuditStatsQueryHandler(
            _repoMock.Object,
            new Mock<ILogger<GetAuditStatsQueryHandler>>().Object);
    }

    private static AuditStatsSnapshot CreateSnapshot() => new()
    {
        TotalInWindow = 42,
        PreviousWindowTotal = 30,
        EventsPerDay =
        [
            new DailyCount(new DateTime(2026, 6, 23), 25),
            new DailyCount(new DateTime(2026, 6, 24), 17)
        ],
        TopActions = [new ReasonCount("user.login", 30), new ReasonCount("user.created", 12)],
        ByEntityType = [new ReasonCount("User", 40), new ReasonCount("unknown", 2)]
    };

    [Fact]
    public async Task Handle_MapsSnapshotToDto()
    {
        _repoMock
            .Setup(r => r.GetAuditStatsAsync(30, "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());

        var result = await _handler.Handle(new GetAuditStatsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Days.Should().Be(30);
        result.Value.TotalInWindow.Should().Be(42);
        result.Value.PreviousWindowTotal.Should().Be(30);
        result.Value.EventsPerDay.Should().HaveCount(2);
        result.Value.EventsPerDay[0].Date.Should().Be(new DateTime(2026, 6, 23));
        result.Value.EventsPerDay[0].Count.Should().Be(25);
        result.Value.TopActions.Should().HaveCount(2);
        result.Value.TopActions[0].Reason.Should().Be("user.login");
        result.Value.TopActions[0].Count.Should().Be(30);
        result.Value.ByEntityType.Should().HaveCount(2);
        result.Value.ByEntityType[1].Reason.Should().Be("unknown");
    }

    [Fact]
    public async Task Handle_TotalsAreWholeWindowNotThePageSum()
    {
        // The series is capped by the day buckets returned, but the totals come
        // from their own aggregate: the dashboard previously derived both from a
        // 100-row page, which under-reported whenever the window was busier.
        _repoMock
            .Setup(r => r.GetAuditStatsAsync(30, "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());

        var result = await _handler.Handle(new GetAuditStatsQuery(), CancellationToken.None);

        result.Value.EventsPerDay.Sum(d => d.Count).Should().Be(42);
        result.Value.TotalInWindow.Should().Be(42);
    }

    [Fact]
    public async Task Handle_PassesRequestedWindowAndZoneToRepository()
    {
        _repoMock
            .Setup(r => r.GetAuditStatsAsync(7, "Europe/Istanbul", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());

        var result = await _handler.Handle(
            new GetAuditStatsQuery(7, "Europe/Istanbul"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Days.Should().Be(7);
        _repoMock.Verify(
            r => r.GetAuditStatsAsync(7, "Europe/Istanbul", It.IsAny<CancellationToken>()),
            Times.Once());
    }
}

public class DashboardQueryValidatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(90)]
    public void Validators_AcceptDaysInRange(int days)
    {
        new GetUserStatsQueryValidator().Validate(new GetUserStatsQuery(days)).IsValid.Should().BeTrue();
        new GetAuthStatsQueryValidator().Validate(new GetAuthStatsQuery(days)).IsValid.Should().BeTrue();
        new GetSessionStatsQueryValidator().Validate(new GetSessionStatsQuery(days)).IsValid.Should().BeTrue();
        new GetAppActivityStatsQueryValidator().Validate(new GetAppActivityStatsQuery(days)).IsValid.Should().BeTrue();
        new GetAuditStatsQueryValidator().Validate(new GetAuditStatsQuery(days)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public void Validators_RejectDaysOutOfRange(int days)
    {
        new GetUserStatsQueryValidator().Validate(new GetUserStatsQuery(days)).IsValid.Should().BeFalse();
        new GetAuthStatsQueryValidator().Validate(new GetAuthStatsQuery(days)).IsValid.Should().BeFalse();
        new GetSessionStatsQueryValidator().Validate(new GetSessionStatsQuery(days)).IsValid.Should().BeFalse();
        new GetAppActivityStatsQueryValidator().Validate(new GetAppActivityStatsQuery(days)).IsValid.Should().BeFalse();
        new GetAuditStatsQueryValidator().Validate(new GetAuditStatsQuery(days)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TimeZoneAwareValidators_AcceptIanaAndRejectWindowsIds()
    {
        new GetUserStatsQueryValidator()
            .Validate(new GetUserStatsQuery(30, "Europe/Istanbul"))
            .IsValid.Should().BeTrue();
        new GetAuthStatsQueryValidator()
            .Validate(new GetAuthStatsQuery(30, "Europe/Istanbul"))
            .IsValid.Should().BeTrue();

        new GetUserStatsQueryValidator()
            .Validate(new GetUserStatsQuery(30, "Turkey Standard Time"))
            .IsValid.Should().BeFalse();
        new GetAuthStatsQueryValidator()
            .Validate(new GetAuthStatsQuery(30, ""))
            .IsValid.Should().BeFalse();

        new GetAuditStatsQueryValidator()
            .Validate(new GetAuditStatsQuery(30, "Europe/Istanbul"))
            .IsValid.Should().BeTrue();
        new GetAuditStatsQueryValidator()
            .Validate(new GetAuditStatsQuery(30, "Turkey Standard Time"))
            .IsValid.Should().BeFalse();
    }
}
