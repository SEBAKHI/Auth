using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Users.ResetUserTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

/// <summary>
/// S08 T9: an administrator's reset of another account's second factor. First
/// the administrator's own session — recent and two factors, by the real
/// reauthentication guard (D-57-1) — before the account is read at all. Never
/// one's own or the system account's — refused before the target's grants are
/// read; never an account whose platform authority the actor's does not cover
/// (no amplification, by the rule every grant obeys); then every credential of
/// the account revoked, then the row and the flag in one store call (row 102
/// (b): a failure leaves the account signed out with its factor, and a retry
/// completes), and the event — naming the actor — after the commit.
/// </summary>
public class ResetUserTwoFactorCommandHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();
    private static readonly Guid ActorSession = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly List<IDomainEvent> _dispatched = [];
    private readonly List<string> _calls = [];

    public ResetUserTwoFactorCommandHandlerTests()
    {
        GivenActorSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        _dispatcher
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback<AggregateRoot, CancellationToken>((root, _) =>
            {
                _calls.Add("dispatch");
                _dispatched.AddRange(root.DomainEvents);
            })
            .Returns(Task.CompletedTask);
        _store.Setup(s => s.TryResetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("reset"))
            .ReturnsAsync(true);
        _revocation
            .Setup(r => r.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("revoke"))
            .ReturnsAsync(2);
    }

    private ResetUserTwoFactorCommandHandler Handler() =>
        new(
            new ReauthenticationGuard(
                _sessions.Object,
                TestHelpers.CreateOptions(new TwoFactorSettings()),
                new FixedTimeProvider(new DateTimeOffset(Now)),
                Mock.Of<ILogger<ReauthenticationGuard>>()),
            _users.Object,
            _permissions.Object,
            new PermissionGrantGuard(_permissions.Object),
            _store.Object,
            _revocation.Object,
            _dispatcher.Object,
            Mock.Of<ILogger<ResetUserTwoFactorCommandHandler>>());

    private static ResetUserTwoFactorCommand Command(Guid target, Guid actor) => new(target, actor, ActorSession);

    private void GivenActorSession(AuthenticationMethods methods, int signedInMinutesAgo = 2) =>
        _sessions.Setup(r => r.GetByIdAsync(ActorSession, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: ActorSession, userId: Actor, createdAt: Now.AddMinutes(-signedInMinutesAgo), methods: methods));

    private void GivenTarget(Guid id, bool flag = true, bool factorRow = true, params string[] platformPermissions)
    {
        _users.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: id, email: "target@example.org", twoFactorEnabled: flag));
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformPermissions);
        _store.Setup(s => s.GetSnapshotAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(factorRow
                ? new TwoFactorSnapshot(id, "v2:secret", "[]", isEnabled: true, failedAttempts: 5, lockedUntil: DateTime.UtcNow.AddMinutes(10))
                : null);
    }

    private void GivenActorHolds(params string[] permissions) =>
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

    private void VerifyNothingChanged()
    {
        _store.Verify(s => s.TryResetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _dispatched.Should().BeEmpty();
    }

    // ── D-57-1: the administrator's own session, before the account is read ──

    [Fact]
    public async Task ActorSessionThatProvedOnlyThePassword_IsRefused_BeforeTheAccountIsRead()
    {
        // S08's own threat: a stolen administrator password, no second factor, the
        // switch off. It must not strip anyone's factor.
        GivenActorSession(AuthenticationMethods.Password);
        GivenTarget(Target);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyTheAccountWasNeverRead();
    }

    [Fact]
    public async Task ActorTwoFactorSessionTooOld_IsRefused_BeforeTheAccountIsRead()
    {
        GivenActorSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp), signedInMinutesAgo: 16);
        GivenTarget(Target);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyTheAccountWasNeverRead();
    }

    private void VerifyTheAccountWasNeverRead()
    {
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _permissions.Verify(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task OwnAccount_IsRefused_BeforeAnyGrantIsRead()
    {
        GivenTarget(Actor, platformPermissions: "users:*");
        GivenActorHolds("*");

        var result = await Handler().Handle(Command(Actor, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.ResetNotPermitted.Code);
        _permissions.Verify(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task SystemAccount_IsRefused_BeforeAnyGrantIsRead()
    {
        GivenTarget(WellKnownUserIds.System);
        GivenActorHolds("*");

        var result = await Handler().Handle(Command(WellKnownUserIds.System, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.ResetNotPermitted.Code);
        _permissions.Verify(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task TargetWithMoreAuthority_IsRefused_NoAmplification()
    {
        // A user-manager (users:*) may not reset a super-admin (*): whoever set the
        // next factor up would hold "*".
        GivenTarget(Target, platformPermissions: "*");
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.ResetNotPermitted.Code);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Forbidden);
        _permissions.Verify(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()), Times.Once,
            "the actor's own permissions are read live, never taken from the token");
        VerifyNothingChanged();
    }

    [Fact]
    public async Task NothingToReset_IsRefused()
    {
        GivenTarget(Target, flag: false, factorRow: false);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task ARowWithoutTheFlag_IsReset()
    {
        // The two sources of truth disagree: the reset repairs them both.
        GivenTarget(Target, flag: false, factorRow: true);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryResetAsync(Target, Actor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_RevokesEveryCredential_ThenRemovesTheFactor_ThenTellsTheOwner_NamingTheActor()
    {
        GivenTarget(Target, platformPermissions: "users:read");
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
        // Row 102 (b): never a factor removed while the sessions it protected live on.
        _calls.Should().Equal("revoke", "reset", "dispatch");
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(
            Target, Actor, "Two-factor authentication reset by an administrator", CancellationToken.None), Times.Once);
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorResetEvent>()
            .Which.Should().Match<TwoFactorResetEvent>(e => e.UserId == Target && e.ResetBy == Actor);
    }

    [Fact]
    public async Task AFailedReset_LeavesTheAccountSignedOutWithItsFactor_AndARetryCompletes()
    {
        GivenTarget(Target);
        GivenActorHolds("users:*");
        _store.SetupSequence(s => s.TryResetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the database went away"))
            .ReturnsAsync(true);

        var first = async () => await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        await first.Should().ThrowAsync<InvalidOperationException>();
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(
            Target, Actor, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "the account is signed out before the factor goes");
        _dispatched.Should().BeEmpty("nothing was reset, so nothing is audited or mailed");

        // The factor row is still there, so the same request, sent again, completes.
        var retry = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        retry.IsError.Should().BeFalse();
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(
            Target, Actor, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorResetEvent>();
    }

    [Fact]
    public async Task AnApplicationUser_WithNoPlatformPermission_CanBeResetByAUserManager()
    {
        GivenTarget(Target);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(Command(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }
}
