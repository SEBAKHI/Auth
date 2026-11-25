using Auth.Application.Common;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.GetTwoFactorStatus;
using Auth.Application.Features.Users.ResetUserTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// AM-S08-1 (ARR-1060): the security page's count of recovery codes left — the
/// length of the stored array of hashes, which loses one entry per code spent;
/// null without an enabled factor. And (owner, 2026-11-24) what the page warns an
/// administrator about: a sign-in by passkey alone, and nobody who could reset
/// their second factor.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the platform-authority guard removed (an
/// account that administers nothing answered the administrator's warning, and the
/// administrators were read for it); the reset's coverage rule skipped (the
/// administrator who does not cover the account's permissions reported as able to
/// reset it). Assertions read a local, never <c>(await …).X.Should()</c>: a failing
/// one written that way hung the test host here (FluentAssertions 8.11, 2026-11-24).
/// </remarks>
public class GetTwoFactorStatusQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly List<PlatformAuthorityHolder> _holders = [];

    public GetTwoFactorStatusQueryHandlerTests()
    {
        // Nobody administers anything unless a test says so.
        _claims.Setup(c => c.ResolveAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _permissions.Setup(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _holders);
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        // Every account has the app unless a test says otherwise.
        _store.Setup(s => s.GetQualifyingFactorsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualifyingSecondFactors(Totp: true, Passkey: false));
    }

    private GetTwoFactorStatusQueryHandler Handler(bool passkeysRun = true)
    {
        var usable = TestHelpers.UsableFactors(_store.Object, passkeysRun);
        var platformAuthority = new EmailCodeFullProofRule(_claims.Object);
        return new(
            _store.Object,
            platformAuthority,
            new PasskeyOnlyAdministratorRule(usable, platformAuthority, Mock.Of<ILogger<PasskeyOnlyAdministratorRule>>()),
            new TwoFactorResetAdministrators(_permissions.Object, new PermissionGrantGuard(_permissions.Object), usable),
            usable,
            Mock.Of<ILogger<GetTwoFactorStatusQueryHandler>>());
    }

    private async Task<TwoFactorStatusResponse> Status(bool passkeysRun = true) =>
        (await Handler(passkeysRun).Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None)).Value;

    private async Task<int?> Remaining() => (await Status()).RecoveryCodesRemaining;

    private void Given(TwoFactorSnapshot? snapshot) =>
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

    private static TwoFactorSnapshot Enabled(string? codes) =>
        new(UserId, "v2:secret", codes, isEnabled: true, failedAttempts: 0, lockedUntil: null);

    /// <summary>The account administers the platform with <paramref name="permissions"/>.</summary>
    private void GivenAdministrator(params string[] permissions)
    {
        _claims.Setup(c => c.ResolveAsync(UserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], permissions, []));
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);
    }

    private void GivenFactors(QualifyingSecondFactors factors) =>
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

    /// <summary>Another account holding <paramref name="codes"/>, with a factor of its own.</summary>
    private void GivenOtherAdministrator(params string[] codes)
    {
        var holder = new PlatformAuthorityHolder(
            Guid.NewGuid(), "other@example.org", "Other", UserStatus.Active, emailConfirmed: true, isDeleted: false, codes);
        _holders.Add(holder);
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(holder.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);
    }

    [Fact]
    public async Task Count_IsTheLengthOfTheStoredSet_AndDropsByOneWhenACodeIsSpent()
    {
        Given(Enabled("[\"h1\",\"h2\",\"h3\",\"h4\",\"h5\",\"h6\",\"h7\",\"h8\",\"h9\",\"h10\"]"));
        var full = await Remaining();
        full.Should().Be(10);

        // What a sign-in with one recovery code leaves stored (the proof's new set).
        Given(Enabled("[\"h1\",\"h2\",\"h3\",\"h4\",\"h5\",\"h6\",\"h7\",\"h8\",\"h9\"]"));
        var afterOne = await Remaining();
        afterOne.Should().Be(9);
    }

    [Fact]
    public async Task NoFactor_OrAPendingOne_IsNull()
    {
        Given(null);
        var withoutFactor = await Remaining();
        withoutFactor.Should().BeNull();

        Given(new TwoFactorSnapshot(UserId, "v2:secret", null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        var pending = await Remaining();
        pending.Should().BeNull("a factor being set up has no codes that work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task EnabledWithoutReadableCodes_IsZero(string? codes)
    {
        Given(Enabled(codes));

        var remaining = await Remaining();
        remaining.Should().Be(0, "none works at sign-in either, so the page asks for new ones");
    }

    [Fact]
    public async Task AnAccountThatAdministersNothing_IsWarnedOfNothing_AndNoAdministratorIsRead()
    {
        Given(null);
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));

        var status = await Status();

        status.SignsInWithPasskeyOnly.Should().BeFalse("a passkey-only account that administers nothing signs in every other way too");
        status.AnotherAdministratorCanReset.Should().BeNull("the warning concerns administrators only");
        _permissions.Verify(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APasskeyOnlyAdministrator_WithNobodyToResetThem_IsWarnedOfBoth()
    {
        Given(null);
        GivenAdministrator("users:read");
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));

        var status = await Status();

        status.SignsInWithPasskeyOnly.Should().BeTrue();
        status.AnotherAdministratorCanReset.Should().BeFalse();
    }

    [Fact]
    public async Task APasskeyOnlyAdministrator_WhilePasskeysDoNotRun_SignsInAsBefore()
    {
        Given(null);
        GivenAdministrator("users:read");
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));

        var status = await Status(passkeysRun: false);

        status.SignsInWithPasskeyOnly.Should().BeFalse("the refusal pauses while passkeys do not run, D-150-4");
    }

    // REVIEW 185 F8. Deliberate break: the factor check removed (the administrators
    // scanned, and an answer given, for an account the reset refuses anyway). Not run on
    // the implementing machine (2026-11-25): Smart App Control blocked the test assembly.
    [Fact]
    public async Task AnAdministratorWithNoSecondFactor_IsNotAskedAbout_AndNoAdministratorIsRead()
    {
        Given(null);
        GivenAdministrator("users:read");
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: false));
        GivenOtherAdministrator("users:*");

        var status = await Status();

        status.AnotherAdministratorCanReset.Should().BeNull("the reset refuses an account with no second factor");
        _permissions.Verify(r => r.GetUsersHoldingPlatformPermissionsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnotherAdministratorWhoCoversTheAccount_CanReset()
    {
        Given(Enabled("[\"h1\"]"));
        GivenAdministrator("users:read");
        GivenOtherAdministrator("users:*");

        var status = await Status();

        status.SignsInWithPasskeyOnly.Should().BeFalse("the account has the app");
        status.AnotherAdministratorCanReset.Should().BeTrue();
    }

    [Fact]
    public async Task AnotherAdministratorWhoDoesNotCoverTheAccountsPermissions_CannotReset()
    {
        Given(null);
        GivenAdministrator("users:read", "roles:*");
        // Holds the reset, but not the account's role management: the reset refuses them.
        GivenOtherAdministrator("users:*");

        var status = await Status();

        status.AnotherAdministratorCanReset.Should().BeFalse();
    }
}
