using System.Text.RegularExpressions;
using Auth.Application.Features.Applications.GetApplicationMfaReadiness;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.ApplicationManagement.Queries;

/// <summary>
/// S23, AM11 (11-3), TS14: the readiness count the console shows before an administrator
/// switches an application's two-step requirement on. One read, over the users who started
/// a session in the application in the last 30 days, with the same definition of "has a
/// qualifying factor" as every sign-in path: an enabled authenticator row. An emailed code
/// never counts.
/// </summary>
/// <remarks>
/// Deliberate break: count the emailed code (<c>OR EmailCodeEnabledAt IS NOT NULL</c>) in the
/// readiness statement only; the predicate test turns red.
/// </remarks>
public class GetApplicationMfaReadinessQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));

    private GetApplicationMfaReadinessQueryHandler Handler() => new(_applications.Object, _store.Object, _clock);

    [Fact]
    public async Task Handle_KnownApplication_ReturnsTheCountsOverThirtyDays()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        _applications.Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(application);
        _store.Setup(s => s.GetApplicationMfaReadinessAsync(application.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationMfaReadiness(ActiveUsers: 12, WithQualifyingFactor: 9));

        var result = await Handler().Handle(new GetApplicationMfaReadinessQuery(application.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ActiveUsers.Should().Be(12);
        result.Value.WithQualifyingFactor.Should().Be(9);
        result.Value.WithoutQualifyingFactor.Should().Be(3);
        result.Value.WindowDays.Should().Be(30);
        _store.Verify(s => s.GetApplicationMfaReadinessAsync(
            application.Id, new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownApplication_ReturnsNotFound_AndCountsNothing()
    {
        var id = Guid.NewGuid();

        var result = await Handler().Handle(new GetApplicationMfaReadinessQuery(id), CancellationToken.None);

        result.FirstError.Code.Should().Be(ApplicationErrors.NotFound(id).Code);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public void Endpoint_DemandsApplicationsRead_AndIsNeverAnonymous()
    {
        // RL3 exception (a) holds only for an administrator endpoint behind [RequirePermission];
        // S14's coverage guard is not merged, so this pins it here. Deliberate break: drop the
        // attribute (the action keeps the controller's [Authorize]).
        var action = typeof(Auth_API.Modules.ApplicationManagement.Controllers.ApplicationsController)
            .GetMethod(nameof(Auth_API.Modules.ApplicationManagement.Controllers.ApplicationsController.GetApplicationMfaReadiness))!;

        action.GetCustomAttributes(typeof(Auth_API.Authorization.RequirePermissionAttribute), inherit: false)
            .Cast<Auth_API.Authorization.RequirePermissionAttribute>()
            .Select(attribute => attribute.Permission)
            .Should().Equal(Auth.Domain.Constants.PermissionCodes.Applications.Read);
        action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: false)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Store_TheReadinessRead_IsOneStatement_WithTheSignInPathsFactorPredicate()
    {
        var applicationId = Guid.NewGuid();
        var since = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new { ActiveUsers = 4, WithQualifyingFactor = 1 });

        var readiness = await new TwoFactorStateStore(db).GetApplicationMfaReadinessAsync(applicationId, since, CancellationToken.None);

        readiness.Should().Be(new ApplicationMfaReadiness(4, 1));
        readiness.WithoutQualifyingFactor.Should().Be(3);
        var command = db.Commands.Should().ContainSingle().Subject;
        var sql = Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ");
        // The factor predicate, word for word the one HasEnabledFactorAsync reads.
        sql.Should().Contain("EXISTS ( SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = a.UserId AND IsEnabled = 1)");
        sql.Should().NotContain("EmailCodeEnabledAt", "an emailed code never qualifies");
        sql.Should().Contain("s.ApplicationId = @ApplicationId AND s.StartedAt >= @Since");
        sql.Should().Contain("usr.IsDeleted = 0");
        sql.Should().Contain("SELECT DISTINCT s.UserId");
        command.Parameters["ApplicationId"].Should().Be(applicationId);
        command.Parameters["Since"].Should().Be(since);
    }
}
