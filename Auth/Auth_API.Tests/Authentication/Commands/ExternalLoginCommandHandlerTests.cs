using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

public class ExternalLoginCommandHandlerTests
{
    private readonly Mock<IExternalAuthProviderFactory> _providerFactoryMock;
    private readonly Mock<IUserExternalLoginRepository> _externalLoginRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IAccountDeletionRequestRepository> _accountDeletionRequestRepositoryMock;
    private readonly Mock<IAccountDeletionTombstoneRepository> _tombstoneRepositoryMock;
    private readonly List<IExternalTokenLifecycle> _tokenLifecycles = [];
    private readonly Mock<IPerUserCryptoService> _perUserCryptoMock = new();
    private readonly Mock<IPersonalOrganizationCreator> _personalOrgCreatorMock;
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock;
    private readonly Mock<ITwoFactorChallengeService> _twoFactorChallengeServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<ExternalLoginCommandHandler>> _loggerMock;
    private readonly Mock<IExternalAuthProvider> _providerMock;
    private readonly ExternalLoginCommandHandler _handler;

    public ExternalLoginCommandHandlerTests()
    {
        _providerFactoryMock = new Mock<IExternalAuthProviderFactory>();
        _externalLoginRepositoryMock = new Mock<IUserExternalLoginRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _accountDeletionRequestRepositoryMock = new Mock<IAccountDeletionRequestRepository>();
        _tombstoneRepositoryMock = new Mock<IAccountDeletionTombstoneRepository>();
        _personalOrgCreatorMock = new Mock<IPersonalOrganizationCreator>();
        _loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        _twoFactorChallengeServiceMock = new Mock<ITwoFactorChallengeService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<ExternalLoginCommandHandler>>();
        _providerMock = new Mock<IExternalAuthProvider>();

        _handler = new ExternalLoginCommandHandler(
            _providerFactoryMock.Object,
            _externalLoginRepositoryMock.Object,
            _userRepositoryMock.Object,
            _accountDeletionRequestRepositoryMock.Object,
            new Auth.Application.Features.Users.Common.IdentifierReservationGuard(
                _tombstoneRepositoryMock.Object, new Mock<IIdentifierHasher>().Object),
            _tokenLifecycles,
            _perUserCryptoMock.Object,
            _personalOrgCreatorMock.Object,
            _loginResponseBuilderMock.Object,
            _twoFactorChallengeServiceMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static ExternalLoginCommand CreateCommand(
        string provider = "google",
        string idToken = "valid-id-token",
        string? nonce = null,
        bool createOrganization = false,
        string? ipAddress = "127.0.0.1",
        string? userAgent = "TestAgent/1.0",
        string? deviceId = null)
        => new(provider, idToken, nonce, createOrganization, ipAddress, userAgent, deviceId);

    private static ExternalUserInfo CreateExternalUserInfo(
        string email = "external@test.com",
        bool emailVerified = true)
        => new("provider-user-123", email, "External", "User", "External User", null, emailVerified);

    private LoginResponse CreateLoginResponse() => new()
    {
        Token = new TokenResponse
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 900,
            RefreshExpiresIn = 604800
        },
        User = new UserInfo
        {
            Id = Guid.NewGuid(),
            Email = "external@test.com",
            FirstName = "External",
            LastName = "User"
        }
    };

    [Fact]
    public async Task Handle_ProviderNotFound_ReturnsError()
    {
        // Arrange
        var command = CreateCommand(provider: "unsupported");
        _providerFactoryMock
            .Setup(f => f.GetProvider("unsupported"))
            .Returns((IExternalAuthProvider?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidIdToken_ReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Validation("Token.Invalid", "Invalid token"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnverifiedEmail_ReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        var externalUser = CreateExternalUserInfo(emailVerified: false);

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingExternalLogin_LogsInAndReturnsResponse()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var externalUser = CreateExternalUserInfo();
        var externalLogin = TestHelpers.CreateUserExternalLogin(userId: userId);
        var loginResponse = CreateLoginResponse();

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync(command.Provider, externalUser.ProviderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalLogin);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(externalLogin.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Token!.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task Handle_TwoFactorEnabled_ReturnsChallengeWithoutTokens()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, twoFactorEnabled: true);
        var externalUser = CreateExternalUserInfo();
        var externalLogin = TestHelpers.CreateUserExternalLogin(userId: userId);

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync(command.Provider, externalUser.ProviderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalLogin);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(externalLogin.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _twoFactorChallengeServiceMock
            .Setup(s => s.CreateChallengeAsync(user, command.IpAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync("challenge-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RequiresTwoFactor.Should().BeTrue();
        result.Value.TwoFactorChallengeToken.Should().Be("challenge-token");
        result.Value.Token.Should().BeNull();
        result.Value.User.Should().BeNull();

        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewExternalUserNoExistingAccount_CreatesUserAndReturnsResponse()
    {
        // Arrange
        var command = CreateCommand();
        var externalUser = CreateExternalUserInfo();
        var loginResponse = CreateLoginResponse();

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync(command.Provider, externalUser.ProviderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserExternalLogin?)null);
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(externalUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(It.IsAny<User>(), command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _externalLoginRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<UserExternalLogin>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NewExternalLoginExistingUser_LinksProviderToExistingAccount()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var existingUser = TestHelpers.CreateUser(id: userId, email: "external@test.com");
        var externalUser = CreateExternalUserInfo();
        var loginResponse = CreateLoginResponse();

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync(command.Provider, externalUser.ProviderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserExternalLogin?)null);
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(externalUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(existingUser, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _externalLoginRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<UserExternalLogin>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_WithCreateOrganization_CreatesOrganizationForNewUser()
    {
        // Arrange
        var command = CreateCommand(createOrganization: true);
        var externalUser = CreateExternalUserInfo();
        var loginResponse = CreateLoginResponse();

        _providerFactoryMock
            .Setup(f => f.GetProvider(command.Provider))
            .Returns(_providerMock.Object);
        _providerMock
            .Setup(p => p.ValidateTokenAsync(command.IdToken, command.Nonce, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalUser);
        _externalLoginRepositoryMock
            .Setup(r => r.GetByProviderAsync(command.Provider, externalUser.ProviderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserExternalLogin?)null);
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(externalUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(It.IsAny<User>(), command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _personalOrgCreatorMock.Verify(
            p => p.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
