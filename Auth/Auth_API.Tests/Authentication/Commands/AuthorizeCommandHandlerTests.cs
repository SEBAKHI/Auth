using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for AuthorizeCommandHandler.
/// </summary>
public class AuthorizeCommandHandlerTests
{
    private const string ClientId = "CRM";
    private const string RedirectUri = "https://app.example.com/callback";
    private const string OriginalUrl = "https://auth.example.com/api/v1/auth/authorize?response_type=code&client_id=CRM";
    private static readonly string ValidChallenge = new('a', 43);

    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IIdpSessionRepository> _idpSessionRepositoryMock = new();
    private readonly Mock<IAuthorizationCodeRepository> _authorizationCodeRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock = new();
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock = new();
    private readonly AuthorizeCommandHandler _handler;

    public AuthorizeCommandHandlerTests()
    {
        _handler = new AuthorizeCommandHandler(
            _applicationRepositoryMock.Object,
            _idpSessionRepositoryMock.Object,
            _authorizationCodeRepositoryMock.Object,
            _userRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            TestHelpers.CreateOptions(new IdentityProviderSettings
            {
                AccountsBaseUrl = "https://accounts.example.com"
            }),
            new Mock<ILogger<AuthorizeCommandHandler>>().Object);
    }

    private static AuthorizeCommand CreateCommand(
        string? responseType = "code",
        string? clientId = ClientId,
        string? redirectUri = RedirectUri,
        string? codeChallenge = null,
        string? codeChallengeMethod = "S256",
        string? state = "xyz",
        string? idpSessionToken = null)
    {
        return new AuthorizeCommand(
            responseType,
            clientId,
            redirectUri,
            codeChallenge ?? ValidChallenge,
            codeChallengeMethod,
            state,
            idpSessionToken,
            OriginalUrl,
            "127.0.0.1");
    }

    private Auth.Domain.Entities.Application SetupApplication(bool isActive = true)
    {
        var application = TestHelpers.CreateApplication(code: ClientId, isActive: isActive);
        application.LoadRedirectUris([RedirectUri]);

        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        return application;
    }

    private Guid SetupValidSession(string idpToken = "idp-token")
    {
        var userId = Guid.NewGuid();

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(idpToken))
            .Returns("idp-hash");
        _idpSessionRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdpSession.Create(userId, "idp-hash", TimeSpan.FromDays(7), null, null));
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        return userId;
    }

    [Fact]
    public async Task Handle_UnknownClient_ReturnsInvalidClientWithoutRedirect()
    {
        // Arrange — no application setup: repository returns null

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task Handle_InactiveClient_ReturnsInvalidClientWithoutRedirect()
    {
        // Arrange
        SetupApplication(isActive: false);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task Handle_UnregisteredRedirectUri_ReturnsInvalidRedirectUriWithoutRedirect()
    {
        // Arrange
        SetupApplication();

        // Act
        var result = await _handler.Handle(
            CreateCommand(redirectUri: "https://evil.example.com/callback"),
            CancellationToken.None);

        // Assert — OAuth hard rule: never redirect to an unregistered target.
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidRedirectUri);
    }

    [Fact]
    public async Task Handle_UnsupportedResponseType_RedirectsBackWithOAuthError()
    {
        // Arrange
        SetupApplication();

        // Act
        var result = await _handler.Handle(CreateCommand(responseType: "token"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith(RedirectUri);
        result.Value.RedirectUrl.Should().Contain("error=unsupported_response_type");
        result.Value.RedirectUrl.Should().Contain("state=xyz");
    }

    [Theory]
    [InlineData(null)]       // missing challenge
    [InlineData("short")]    // malformed challenge
    public async Task Handle_MissingOrMalformedCodeChallenge_RedirectsBackWithInvalidRequest(string? challenge)
    {
        // Arrange
        SetupApplication();

        // A null theory value means "send an empty challenge" (the factory
        // substitutes the valid default for null).
        var command = new AuthorizeCommand(
            "code", ClientId, RedirectUri, challenge, "S256", "xyz", null, OriginalUrl, "127.0.0.1");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Contain("error=invalid_request");
    }

    [Fact]
    public async Task Handle_PlainCodeChallengeMethod_RedirectsBackWithInvalidRequest()
    {
        // Arrange
        SetupApplication();

        // Act — "plain" is deliberately unsupported.
        var result = await _handler.Handle(
            CreateCommand(codeChallengeMethod: "plain"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Contain("error=invalid_request");
    }

    [Fact]
    public async Task Handle_NoIdpSession_RedirectsToAccountsLoginWithReturnTo()
    {
        // Arrange
        SetupApplication();

        // Act
        var result = await _handler.Handle(CreateCommand(idpSessionToken: null), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login?returnTo=");
        result.Value.RedirectUrl.Should().Contain(Uri.EscapeDataString(OriginalUrl));
    }

    [Fact]
    public async Task Handle_UnknownIdpSessionToken_RedirectsToAccountsLogin()
    {
        // Arrange
        SetupApplication();
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash("stale-token"))
            .Returns("stale-hash");
        _idpSessionRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("stale-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession?)null);

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "stale-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LockedUser_RedirectsToAccountsLoginInsteadOfIssuingCode()
    {
        // Arrange
        SetupApplication();
        var userId = Guid.NewGuid();
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash("idp-token"))
            .Returns("idp-hash");
        _idpSessionRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdpSession.Create(userId, "idp-hash", TimeSpan.FromDays(7), null, null));
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, status: UserStatus.Locked));

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        _authorizationCodeRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidSession_IssuesCodeAndRedirectsToClient()
    {
        // Arrange
        SetupApplication();
        var userId = SetupValidSession();

        _jwtTokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns("plain-code");
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");

        AuthorizationCode? savedCode = null;
        _authorizationCodeRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationCode, CancellationToken>((c, _) => savedCode = c)
            .ReturnsAsync((AuthorizationCode c, CancellationToken _) => c);

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        result.Value.RedirectUrl.Should().Contain("state=xyz");

        savedCode.Should().NotBeNull();
        savedCode!.UserId.Should().Be(userId);
        savedCode.CodeHash.Should().Be("code-hash");
        savedCode.RedirectUri.Should().Be(RedirectUri);
        savedCode.CodeChallenge.Should().Be(ValidChallenge);
        savedCode.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RedirectUriWithExistingQuery_AppendsCodeWithAmpersand()
    {
        // Arrange
        const string uriWithQuery = "https://app.example.com/callback?tenant=t1";
        var application = TestHelpers.CreateApplication(code: ClientId);
        application.LoadRedirectUris([uriWithQuery]);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        SetupValidSession();
        _jwtTokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns("plain-code");
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode c, CancellationToken _) => c);

        // Act
        var result = await _handler.Handle(
            CreateCommand(redirectUri: uriWithQuery, idpSessionToken: "idp-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{uriWithQuery}&code=plain-code");
    }
}
