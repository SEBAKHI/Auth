using System.Text.RegularExpressions;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.OrganizationSetup;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, LK2.5 (owner decision RV-P6-6, 2026-09-27): a session that already exists
/// keeps renewing while the account holds a lock raised by wrong passwords, timed
/// or hard; only a live administrator's lock refuses (and refresh revokes, as
/// before). Renewal proves nothing about the password, so refusing it protected
/// nothing, and let anyone who knew the address sign the owner out of every
/// device with five wrong guesses. Renewal never lifts the lock either
/// (BuildAsyncCallersRepairGuardTests, the exchange passes false).
/// </summary>
/// <remarks>
/// <para>
/// One rule, five consumers: refresh, authorize and the code exchange, and since
/// the card was written userinfo (X11) and the organization set-up session (OI-63).
/// The rule lives in <see cref="User.CanRenewCredentials"/>; the handler-level cases
/// sit with each handler's own tests (RefreshTokenCommandHandlerTests,
/// AuthorizeCommandHandlerTests, ExchangeAuthorizationCodeCommandHandlerTests,
/// GetOidcUserInfoQueryHandlerTests, all named *AutomaticallyLockedUser*), and the
/// set-up session's is below.
/// </para>
/// <para>
/// Deliberate break: decide renewal by <c>IsLockedOut()</c> again (any live lock
/// refuses). The timed and hard rows below, and every handler case, turn red.
/// </para>
/// </remarks>
public class RenewalDuringAutomaticLockTests
{
    public static TheoryData<UserStatus, int, int?, bool> Accounts => new()
    {
        // status, count, expiry in minutes (null = none), may renew
        { UserStatus.Active, 0, null, true },
        { UserStatus.Active, 4, null, true },
        { UserStatus.Locked, 5, 15, true },     // timed
        { UserStatus.Locked, 100, null, true }, // hard
        { UserStatus.Locked, 5, -1, true },     // lapsed
        { UserStatus.Locked, 0, null, false },  // administrator, no expiry
        { UserStatus.Locked, 0, 60, false },    // administrator, timed
        { UserStatus.Inactive, 0, null, false },
        { UserStatus.Pending, 0, null, false },
    };

    [Fact]
    public void Accounts_IsNotEmpty() => Accounts.Count.Should().BeGreaterThan(5);

    [Theory]
    [MemberData(nameof(Accounts))]
    public void CanRenewCredentials_RefusesOnlyAnAdministratorsLockAndOffboardedStatuses(
        UserStatus status, int count, int? expiresInMinutes, bool expected)
    {
        var user = TestHelpers.CreateUser(
            status: status,
            failedLoginAttempts: count,
            lockoutEnd: expiresInMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);

        user.CanRenewCredentials().Should().Be(expected);
    }

    /// <summary>The five consumers of the renewal rule, by file.</summary>
    private static readonly string[] Consumers =
    [
        "RefreshTokenCommandHandler.cs",
        "AuthorizeCommandHandler.cs",
        "ExchangeAuthorizationCodeCommandHandler.cs",
        "GetOidcUserInfoQueryHandler.cs",
        "OrganizationSetupSession.cs",
    ];

    [Fact]
    public void EveryRenewalPath_DecidesByTheOneRule()
    {
        var sources = ApiSourceScan.ProductionSources()
            .Where(file => Regex.IsMatch(file.Source, @"\.CanRenewCredentials\(\)"))
            .ToList();

        sources.Select(file => Path.GetFileName(file.File)).Should().BeEquivalentTo(Consumers,
            "a new renewal path must use the same rule and be listed here");

        // None of them second-guesses the rule with a lock check of its own.
        sources.Should().AllSatisfy(file =>
            Regex.IsMatch(file.Source, @"\.(IsLockedOut|LockKind)\(").Should().BeFalse(
                $"{Path.GetFileName(file.File)} must decide renewal by CanRenewCredentials alone"));
    }

    [Theory]
    [InlineData(5, 15, true)]
    [InlineData(100, null, true)]
    [InlineData(0, null, false)]
    public async Task OrganizationSetupSession_ResolvesTheUserUnlessAnAdministratorLockedIt(
        int count, int? lockedForMinutes, bool resolved)
    {
        var user = TestHelpers.CreateUser(
            status: UserStatus.Locked,
            failedLoginAttempts: count,
            lockoutEnd: lockedForMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(k => k.ComputeTokenHash("idp-token")).Returns("idp-hash");
        var sessions = new Mock<IIdpSessionRepository>();
        sessions
            .Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdpSession(
                Guid.NewGuid(), user.Id, "idp-hash", DateTime.UtcNow, DateTime.UtcNow.AddDays(7), null, null, null));
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var session = new OrganizationSetupSession(
            sessions.Object, keys.Object, users.Object, new Mock<IApplicationRepository>().Object,
            new OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
            new Mock<IAuthorizationRequestRepository>().Object,
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TimeProvider.System,
            new Mock<Microsoft.Extensions.Logging.ILogger<OrganizationSetupSession>>().Object);

        var found = await session.ResolveUserAsync(
            "idp-token", clientId: null, authorizationRequestHandle: null, CancellationToken.None);

        (found is not null).Should().Be(resolved);
    }
}
