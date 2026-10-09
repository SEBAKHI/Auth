using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S08 T10: while <c>TwoFactor:EnforceForPlatformAdmins</c> is on, a platform
/// administrator cannot switch the second factor off. The order is fixed: X02's
/// recent-sign-in check first, then the policy, and only then — never before — an
/// attempt is reserved on the factor. Everyone else keeps X02's behaviour.
/// </summary>
public class DisableTwoFactorPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<ISecondFactorVerifier> _verifier = new(MockBehavior.Strict);
    private readonly Mock<ITwoFactorStateStore> _store = new(MockBehavior.Strict);
    private readonly TwoFactorSettings _settings = new();
    private readonly List<string> _calls = [];

    public DisableTwoFactorPolicyTests()
    {
        _guard.Setup(g => g.EnsureRecentSignInAsync(UserId, SessionId, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("guard"))
            .ReturnsAsync(new RecentSession(SessionId, "Edge on Windows"));
    }

    private DisableTwoFactorCommandHandler Handler()
    {
        var policy = new PlatformMfaPolicy(
            Mock.Of<ITwoFactorStateStore>(),
            _claims.Object,
            TestHelpers.CreateOptions(_settings),
            TestHelpers.LoadedSettingsReloader(),
            new EnforcedSettingsWarning(),
            Mock.Of<ILogger<PlatformMfaPolicy>>());

        return new DisableTwoFactorCommandHandler(
            _guard.Object,
            policy,
            _verifier.Object,
            _store.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_settings)),
            Mock.Of<IUserRepository>(),
            Mock.Of<ICredentialRevocationService>(),
            Mock.Of<IDomainEventDispatcher>(),
            Mock.Of<ILogger<DisableTwoFactorCommandHandler>>());
    }

    private void Resolves(params string[] platformPermissions) =>
        _claims.Setup(r => r.ResolveAsync(UserId, null, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("policy"))
            .ReturnsAsync(new TokenClaims([], platformPermissions, []));

    private static DisableTwoFactorCommand Command() =>
        new(UserId, "123456", false, SessionId, "idp-cookie", "203.0.113.7");

    [Fact]
    public async Task Handle_PlatformAdministrator_UnderEnforcement_IsRefused_BeforeAnyReservation()
    {
        _settings.EnforceForPlatformAdmins = true;
        Resolves("users:read");

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredByPolicy.Code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        _calls.Should().Equal("guard", "policy");
        _verifier.VerifyNoOtherCalls();
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_StaleSession_IsAskedToSignInAgain_BeforeThePolicyIsAsked()
    {
        _settings.EnforceForPlatformAdmins = true;
        Resolves("users:read");
        _guard.Setup(g => g.EnsureRecentSignInAsync(UserId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _claims.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_NoPlatformPermission_UnderEnforcement_ReachesTheReservation()
    {
        // Two-step stays optional for everyone else: the request goes on to X02's
        // reservation (whose answer here is the strict mock's — reached at all is
        // the point).
        _settings.EnforceForPlatformAdmins = true;
        Resolves();
        _verifier.Setup(v => v.ReserveAsync(UserId, true, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("reserve"))
            .ReturnsAsync(UserErrors.TwoFactorNotEnabled);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
        _calls.Should().Equal("guard", "policy", "reserve");
    }

    [Fact]
    public async Task Handle_SwitchOff_PlatformAdministrator_KeepsX02Behaviour_WithoutReadingClaims()
    {
        _verifier.Setup(v => v.ReserveAsync(UserId, true, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("reserve"))
            .ReturnsAsync(UserErrors.TwoFactorNotEnabled);

        await Handler().Handle(Command(), CancellationToken.None);

        _calls.Should().Equal("guard", "reserve");
        _claims.VerifyNoOtherCalls();
    }
}
