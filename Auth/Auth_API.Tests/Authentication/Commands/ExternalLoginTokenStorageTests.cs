using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for the Apple refresh-token storage during external sign-in:
/// the exchange is best-effort (a failure never blocks the login), and a
/// successful exchange stores the token encrypted under the user's DEK.
/// </summary>
public class ExternalLoginTokenStorageTests
{
    private readonly Mock<IExternalAuthProviderFactory> _providerFactoryMock = new();
    private readonly Mock<IExternalAuthProvider> _providerMock = new();
    private readonly Mock<IUserExternalLoginRepository> _externalLoginRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IExternalTokenLifecycle> _tokenLifecycleMock = new();
    private readonly Mock<IPerUserCryptoService> _perUserCryptoMock = new();
    private readonly ExternalLoginCommandHandler _handler;
    private readonly Auth.Domain.Entities.UserExternalLogin _existingLogin;
    private readonly Auth.Domain.Entities.User _user;

    public ExternalLoginTokenStorageTests()
    {
        _user = TestHelpers.CreateUser();
        _existingLogin = TestHelpers.CreateUserExternalLogin(
            userId: _user.Id, provider: "apple", providerUserId: "apple-sub-001");

        _providerFactoryMock.Setup(f => f.GetProvider("apple")).Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo(
                "apple-sub-001", _user.Email, "", "", null, null, EmailVerified: true));
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync("apple", "apple-sub-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_existingLogin);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
        _tokenLifecycleMock.SetupGet(l => l.ProviderName).Returns("apple");

        var loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>(), true, null, null))
            .ReturnsAsync(new LoginResponse());

        _handler = new ExternalLoginCommandHandler(
            _providerFactoryMock.Object,
            _externalLoginRepositoryMock.Object,
            _userRepositoryMock.Object,
            new Mock<IPermissionRepository>().Object,
            new Mock<IAccountDeletionRequestRepository>().Object,
            new IdentifierReservationGuard(
                new Mock<IAccountDeletionTombstoneRepository>().Object,
                new Mock<IIdentifierHasher>().Object),
            [_tokenLifecycleMock.Object],
            _perUserCryptoMock.Object,
            new Mock<IExternalAvatarImporter>().Object,
            new Mock<IPersonalOrganizationCreator>().Object,
            loginResponseBuilderMock.Object,
            new Mock<ITwoFactorChallengeService>().Object,
            TestHelpers.CreateExternalNonceGuard(),
            new Mock<IDomainEventDispatcher>().Object,
            new Mock<ILoginAttemptRepository>().Object,
            TestHelpers.CreateOptions(new Auth.Application.Configuration.PasswordSettings()),
            TestHelpers.CreateOptions(new Auth.Application.Configuration.RegistrationSettings()),
            new Mock<ILogger<ExternalLoginCommandHandler>>().Object);
    }

    private static ExternalLoginCommand CreateCommand(string? authorizationCode) => new(
        Provider: "apple",
        IdToken: "valid-id-token",
        Nonce: null,
        AuthorizationCode: authorizationCode);

    [Fact]
    public async Task Handle_SuccessfulExchange_StoresTheTokenEncryptedUnderTheUsersDek()
    {
        _tokenLifecycleMock
            .Setup(l => l.ExchangeCodeAsync("auth-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync("rt-plain");
        _perUserCryptoMock
            .Setup(c => c.EncryptAsync(
                _user.Id, "rt-plain", EncryptedFieldPurpose.ExternalProviderRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v2:enc");

        var result = await _handler.Handle(CreateCommand("auth-code"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _externalLoginRepositoryMock.Verify(
            r => r.UpdateProviderRefreshTokenAsync(_existingLogin.Id, "v2:enc", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_FailedExchange_SignsInAnywayWithoutStoringAnything()
    {
        _tokenLifecycleMock
            .Setup(l => l.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _handler.Handle(CreateCommand("expired-code"), CancellationToken.None);

        result.IsError.Should().BeFalse("a failed exchange must never break the sign-in");
        _externalLoginRepositoryMock.Verify(
            r => r.UpdateProviderRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoAuthorizationCode_NeverTouchesTheLifecycle()
    {
        var result = await _handler.Handle(CreateCommand(authorizationCode: null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _tokenLifecycleMock.Verify(
            l => l.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
