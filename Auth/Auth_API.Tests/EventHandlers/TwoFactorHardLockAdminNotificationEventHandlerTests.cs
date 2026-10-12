using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.ResetUserTwoFactor;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Modules.Authentication.EventHandlers;
using Auth_API.Tests.Authentication;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// X12 PR B, LK8.5 and the C5 row «2FA hard lock»: when an account's second factor
/// reaches the consecutive-failure limit, its owner and the administrators who could
/// reset it now are told by email (owner decision D-X12B-1 (a)) — each in a message of
/// their own — and nothing about the notice can fail the request that caused it.
/// </summary>
/// <remarks>
/// The eligibility rule is the endpoint's own: <c>users:reset-two-factor</c> by the
/// wildcard match every check uses, no amplification (PermissionGrantGuard, real here,
/// over the repository mock), an active, confirmed, live account that is not the
/// locked one, at most ten. Deliberate break: send on the password's hard lock too.
/// </remarks>
public class TwoFactorHardLockAdminNotificationEventHandlerTests
{
    private static readonly Guid Target = Guid.NewGuid();
    private static readonly DateTimeOffset LockedAt = new(2026, 10, 12, 9, 14, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly LockSignalTests.CapturingLogger<TwoFactorHardLockAdminNotificationEventHandler> _log = new();
    private readonly List<NotificationRequest> _sent = [];
    private readonly List<PlatformAuthorityHolder> _holders = [];

    public TwoFactorHardLockAdminNotificationEventHandlerTests()
    {
        _users.Setup(r => r.GetByIdIncludeDeletedAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: Target, email: "owner@example.org", firstName: "Olivia", lastName: "Owner"));
        _permissions.Setup(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _holders);
        // The locked account holds no platform permission unless a test says so.
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _notifications
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => _sent.Add(request))
            .ReturnsAsync(Result.Success);
    }

    private TwoFactorHardLockAdminNotificationEventHandler Handler() =>
        new(
            _users.Object,
            new TwoFactorResetAdministrators(_permissions.Object, new PermissionGrantGuard(_permissions.Object)),
            _notifications.Object,
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://console.example.com/" }),
            _log);

    private static UserHardLockedEvent Locked(HardLockedFactor factor = HardLockedFactor.SecondFactor) =>
        new(Guid.NewGuid(), Target, factor, 100, null, LockedAt);

    private PlatformAuthorityHolder Holder(
        string email,
        string[] codes,
        UserStatus status = UserStatus.Active,
        bool confirmed = true,
        bool deleted = false,
        Guid? id = null)
    {
        var holder = new PlatformAuthorityHolder(id ?? Guid.NewGuid(), email, email.Split('@')[0], status, confirmed, deleted, codes);
        _holders.Add(holder);
        // PermissionGrantGuard reads each candidate's own grants live.
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(holder.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);
        return holder;
    }

    private IEnumerable<NotificationRequest> To(string audience) =>
        _sent.Where(request => (string?)request.Variables["Audience"] == audience);

    [Fact]
    public async Task ThePasswordsHardLock_IsNotThisHandlers()
    {
        Holder("admin@example.org", ["users:*"]);

        await Handler().Handle(Locked(HardLockedFactor.Password), CancellationToken.None);

        _sent.Should().BeEmpty("the password's hard lock has its own exits and no administrator step");
        _users.Verify(r => r.GetByIdIncludeDeletedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheOwnerAndEachEligibleAdministrator_GetTheirOwnMessage()
    {
        var admin = Holder("admin@example.org", ["users:*"]);

        await Handler().Handle(Locked(), CancellationToken.None);

        _sent.Should().HaveCount(2);
        _sent.Should().OnlyContain(request =>
            request.TypeCode == NotificationTypeCodes.TwoFactorHardLocked && request.ApplicationId == null);

        var owner = To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Owner).Should().ContainSingle().Subject;
        owner.RecipientAddress.Should().Be("owner@example.org");
        owner.RecipientUserId.Should().Be(Target);

        var administrator = To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator).Should().ContainSingle().Subject;
        administrator.RecipientAddress.Should().Be("admin@example.org");
        administrator.RecipientUserId.Should().Be(admin.UserId);
        administrator.Variables["UserName"].Should().Be("admin");
        administrator.Variables["AccountName"].Should().Be("Olivia Owner");
        administrator.Variables["AccountId"].Should().Be(Target.ToString());
        administrator.Variables["AccountLink"].Should().Be($"https://console.example.com/users/{Target}");
        administrator.Variables["FailedAttempts"].Should().Be(100);
        administrator.Variables["OccurredAtUtc"].Should().Be("2026-10-12 09:14:00Z");
        administrator.Variables["ResetPasswordLink"].Should().Be("https://console.example.com/forgot-password");
    }

    [Fact]
    public async Task TheOwnersMessage_NeverNamesAnAdministrator_AndNoMessageCarriesMoreThanTheOwnersCould()
    {
        Holder("admin@example.org", ["users:*"]);

        await Handler().Handle(Locked(), CancellationToken.None);

        var owner = To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Owner).Single();
        var administrator = To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator).Single();
        owner.Variables.Values.OfType<string>().Should().NotContain(value => value.Contains("admin", StringComparison.Ordinal),
            "the owner's message does not list the administrators");
        administrator.Variables.Keys.Should().BeEquivalentTo(owner.Variables.Keys,
            "an administrator's message carries nothing the owner's could not");
        owner.Variables.Keys.Should().BeEquivalentTo(
            ["UserName", "Audience", "AccountName", "AccountId", "AccountLink", "FailedAttempts", "OccurredAtUtc", "ResetPasswordLink"],
            "the catalog of the seeded type: no address, device or code");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("users:*")]
    [InlineData("users:reset-two-factor")]
    public async Task AnAdministratorHoldingTheResetPermission_ByAnyWildcard_IsTold(string code)
    {
        Holder("admin@example.org", [code]);

        await Handler().Handle(Locked(), CancellationToken.None);

        To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator).Should().ContainSingle();
    }

    [Fact]
    public async Task OnlyAdministratorsWhoCouldActNow_AreTold()
    {
        Holder("lacks-the-code@example.org", ["roles:*", "users:read"]);
        Holder("inactive@example.org", ["users:*"], status: UserStatus.Inactive);
        Holder("locked@example.org", ["users:*"], status: UserStatus.Locked);
        Holder("deleted@example.org", ["users:*"], deleted: true);
        Holder("unconfirmed@example.org", ["users:*"], confirmed: false);
        // The locked account itself, an administrator too: never its own resetter.
        // (Listed directly: its live grants stay the ones the fixture gave it.)
        _holders.Add(new PlatformAuthorityHolder(Target, "owner@example.org", "Olivia Owner", UserStatus.Active, true, false, ["users:*"]));
        var eligible = Holder("eligible@example.org", ["users:*"]);

        await Handler().Handle(Locked(), CancellationToken.None);

        To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator)
            .Select(request => request.RecipientUserId).Should().Equal([eligible.UserId]);
        To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Owner).Should().ContainSingle(
            "the owner hears once, as the owner, even while holding platform authority");
    }

    [Fact]
    public async Task AnAdministratorWithLessAuthorityThanTheAccount_IsNotTold()
    {
        // No amplification: the reset would refuse them, so they are not asked.
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["roles:*"]);
        Holder("user-manager@example.org", ["users:*"]);
        var superAdmin = Holder("super@example.org", ["*"]);

        await Handler().Handle(Locked(), CancellationToken.None);

        To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator)
            .Select(request => request.RecipientUserId).Should().Equal([superAdmin.UserId]);
    }

    [Fact]
    public async Task AtMostTenAdministrators_AreTold()
    {
        for (var i = 0; i < 15; i++)
        {
            Holder($"admin{i}@example.org", ["users:*"]);
        }

        await Handler().Handle(Locked(), CancellationToken.None);

        To(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator).Should().HaveCount(TwoFactorResetAdministrators.MaxRecipients);
        TwoFactorResetAdministrators.MaxRecipients.Should().Be(10);
    }

    [Fact]
    public async Task NoAdministratorLeft_TellsTheOwner_AndSaysSoAtWarning()
    {
        await Handler().Handle(Locked(), CancellationToken.None);

        _sent.Should().ContainSingle().Which.Variables["Audience"].Should().Be("owner");
        var line = _log.Entries.Should().ContainSingle(e => e.Values.ContainsKey("AdministratorCount")).Subject;
        line.Level.Should().Be(LogLevel.Warning, "with nobody to reset it the exit is the owner's emergency script");
        line.Values["AdministratorCount"].Should().Be(0);
    }

    [Fact]
    public async Task OneRecipientsFailure_CostsTheOthersNothing_AndNothingIsThrown()
    {
        Holder("admin@example.org", ["users:*"]);
        _notifications
            .Setup(s => s.SendAsync(It.Is<NotificationRequest>(r => r.RecipientUserId == Target), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox down"));

        var act = () => Handler().Handle(Locked(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _sent.Should().ContainSingle().Which.Variables["Audience"].Should().Be("administrator");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AFailedRead_IsLogged_NeverThrown()
    {
        _permissions.Setup(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => Handler().Handle(Locked(), CancellationToken.None);

        await act.Should().NotThrowAsync("the lock is written; a notice must not turn the answer into an error");
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ItsOwnCancellation_IsLetThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _permissions.Setup(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Handler().Handle(Locked(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnAccountThatIsGone_SendsNothing()
    {
        _users.Setup(r => r.GetByIdIncludeDeletedAsync(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);
        Holder("admin@example.org", ["users:*"]);

        await Handler().Handle(Locked(), CancellationToken.None);

        _sent.Should().BeEmpty();
    }
}
