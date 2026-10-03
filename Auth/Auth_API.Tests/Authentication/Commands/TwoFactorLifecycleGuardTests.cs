using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The recent-sign-in check runs FIRST in every handler that changes the second
/// factor: before a secret is read, generated or returned, before an attempt is
/// reserved, before anything is written. Every other dependency here is a strict
/// mock with nothing set up, so any call a handler makes past a refused check
/// fails the test.
/// </summary>
public class TwoFactorLifecycleGuardTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IReauthenticationGuard> _guard = new(MockBehavior.Strict);
    private readonly Mock<ISecondFactorVerifier> _verifier = new(MockBehavior.Strict);
    private readonly Mock<ITwoFactorStateStore> _stateStore = new(MockBehavior.Strict);
    private readonly Mock<ITotpService> _totpService = new(MockBehavior.Strict);
    private readonly Mock<ITwoFactorSecretProtector> _secretProtector = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _userRepository = new(MockBehavior.Strict);
    private readonly Mock<IPlatformSettingsRepository> _platformSettings = new(MockBehavior.Strict);
    private readonly Mock<ICredentialRevocationService> _revocation = new(MockBehavior.Strict);
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new(MockBehavior.Strict);

    public TwoFactorLifecycleGuardTests()
    {
        _guard
            .Setup(g => g.EnsureRecentSignInAsync(UserId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);
    }

    private Task<IErrorOr> Run(string handler)
    {
        var replayPolicy = new TotpReplayPolicy(TestHelpers.CreateOptions(new TwoFactorSettings()));

        // The email proof applies (email on), and every one of its dependencies is
        // strict too: no read of the factor or the code table may precede the check.
        var emailProofPolicy = new FirstFactorEmailProofPolicy(
            TestHelpers.CreateOptions(new TwoFactorSettings()),
            TestHelpers.CreateOptions(new EmailSettings { Enabled = true }));
        var emailProof = new FirstFactorEmailProof(
            new Mock<ITwoFactorBindCodeRepository>(MockBehavior.Strict).Object,
            _userRepository.Object,
            new Mock<INotificationService>(MockBehavior.Strict).Object,
            new Mock<IOtpGenerator>(MockBehavior.Strict).Object,
            new Mock<IOtpHasher>(MockBehavior.Strict).Object,
            TestHelpers.CreateOptions(new EmailSettings { Enabled = true }),
            TimeProvider.System,
            Mock.Of<ILogger<FirstFactorEmailProof>>());

        return handler switch
        {
            "setup" => Send(new SetupTwoFactorCommandHandler(
                    _guard.Object,
                    _userRepository.Object,
                    _stateStore.Object,
                    _secretProtector.Object,
                    _platformSettings.Object,
                    _totpService.Object,
                    emailProofPolicy,
                    TestHelpers.CreateOptions(new JwtSettings { Issuer = "https://auth.example.com" }),
                    Mock.Of<ILogger<SetupTwoFactorCommandHandler>>())
                .Handle(new SetupTwoFactorCommand(UserId, SessionId), CancellationToken.None)),

            "enable" => Send(new EnableTwoFactorCommandHandler(
                    _guard.Object,
                    _verifier.Object,
                    _stateStore.Object,
                    _totpService.Object,
                    replayPolicy,
                    emailProofPolicy,
                    emailProof,
                    _userRepository.Object,
                    _dispatcher.Object,
                    Mock.Of<ILogger<EnableTwoFactorCommandHandler>>())
                .Handle(new EnableTwoFactorCommand(UserId, "123456", SessionId, "203.0.113.7"), CancellationToken.None)),

            "disable" => Send(new DisableTwoFactorCommandHandler(
                    _guard.Object,
                    _verifier.Object,
                    _stateStore.Object,
                    replayPolicy,
                    _userRepository.Object,
                    _revocation.Object,
                    _dispatcher.Object,
                    Mock.Of<ILogger<DisableTwoFactorCommandHandler>>())
                .Handle(new DisableTwoFactorCommand(UserId, "123456", false, SessionId, "idp-cookie", "203.0.113.7"), CancellationToken.None)),

            _ => throw new ArgumentOutOfRangeException(nameof(handler), handler, null),
        };
    }

    private static async Task<IErrorOr> Send<T>(Task<ErrorOr<T>> call) => await call;

    [Theory]
    [InlineData("setup")]
    [InlineData("enable")]
    [InlineData("disable")]
    public async Task StaleSession_Reauthenticates_BeforeAnyEffect(string handler)
    {
        var result = await Run(handler);

        result.IsError.Should().BeTrue();
        result.Errors!.Should().ContainSingle();
        result.Errors![0].Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        result.Errors![0].Type.Should().Be(ErrorType.Forbidden,
            "403, never 401: a 401 makes the client refresh and replay, and the refreshed token belongs to the same old session");
        _guard.Verify(g => g.EnsureRecentSignInAsync(UserId, SessionId, It.IsAny<CancellationToken>()), Times.Once);

        // Nothing else was called: strict mocks would have thrown, and these say so.
        _verifier.VerifyNoOtherCalls();
        _stateStore.VerifyNoOtherCalls();
        _totpService.VerifyNoOtherCalls();
        _secretProtector.VerifyNoOtherCalls();
        _userRepository.VerifyNoOtherCalls();
        _platformSettings.VerifyNoOtherCalls();
        _revocation.VerifyNoOtherCalls();
        _dispatcher.VerifyNoOtherCalls();
    }
}
