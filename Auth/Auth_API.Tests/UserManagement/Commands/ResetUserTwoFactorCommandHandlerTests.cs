using Auth.Application.Common;
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
/// S08 T9: an administrator's reset of another account's second factor. Never
/// one's own or the system account's — refused before the target's grants are
/// read; never an account whose platform authority the actor's does not cover
/// (no amplification, by the rule every grant obeys); then the row and the flag
/// in one store call, every credential of the account revoked, and the event —
/// naming the actor — after the commit.
/// </summary>
public class ResetUserTwoFactorCommandHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly List<IDomainEvent> _dispatched = [];
    private readonly List<string> _calls = [];

    public ResetUserTwoFactorCommandHandlerTests()
    {
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
            _users.Object,
            _permissions.Object,
            new PermissionGrantGuard(_permissions.Object),
            _store.Object,
            _revocation.Object,
            _dispatcher.Object,
            Mock.Of<ILogger<ResetUserTwoFactorCommandHandler>>());

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

    [Fact]
    public async Task OwnAccount_IsRefused_BeforeAnyGrantIsRead()
    {
        GivenTarget(Actor, platformPermissions: "users:*");
        GivenActorHolds("*");

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Actor, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.ResetNotPermitted.Code);
        _permissions.Verify(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task SystemAccount_IsRefused_BeforeAnyGrantIsRead()
    {
        GivenTarget(WellKnownUserIds.System);
        GivenActorHolds("*");

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(WellKnownUserIds.System, Actor), CancellationToken.None);

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

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Target, Actor), CancellationToken.None);

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

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Target, Actor), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
        VerifyNothingChanged();
    }

    [Fact]
    public async Task ARowWithoutTheFlag_IsReset()
    {
        // The two sources of truth disagree: the reset repairs them both.
        GivenTarget(Target, flag: false, factorRow: true);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryResetAsync(Target, Actor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_RemovesTheFactor_RevokesEveryCredential_ThenTellsTheOwner_NamingTheActor()
    {
        GivenTarget(Target, platformPermissions: "users:read");
        GivenActorHolds("users:*");

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _calls.Should().Equal("reset", "revoke", "dispatch");
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(
            Target, Actor, "Two-factor authentication reset by an administrator", CancellationToken.None), Times.Once);
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorResetEvent>()
            .Which.Should().Match<TwoFactorResetEvent>(e => e.UserId == Target && e.ResetBy == Actor);
    }

    [Fact]
    public async Task AnApplicationUser_WithNoPlatformPermission_CanBeResetByAUserManager()
    {
        GivenTarget(Target);
        GivenActorHolds("users:*");

        var result = await Handler().Handle(new ResetUserTwoFactorCommand(Target, Actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }
}
