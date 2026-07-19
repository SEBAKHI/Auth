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
        string? idpSessionToken = null,
        string? prompt = null,
        string? maxAge = null)
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
            "127.0.0.1",
            prompt,
            maxAge);
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
        return SetupSessionAged(TimeSpan.Zero, idpToken);
    }

    /// <summary>
    /// Registers a valid, non-revoked IdP session whose CreatedAt is <paramref name="age"/>
    /// in the past, so step-up policies that compare session age can be exercised.
    /// </summary>
    private Guid SetupSessionAged(TimeSpan age, string idpToken = "idp-token")
    {
        var userId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow - age;
        var session = new IdpSession(
            Guid.NewGuid(), userId, "idp-hash", createdAt, createdAt.AddDays(7), null, null, null);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(idpToken))
            .Returns("idp-hash");
        _idpSessionRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        return userId;
    }

    /// <summary>Sets up the mocks needed for a successful authorization-code issuance.</summary>
    private void SetupCodeIssuance()
    {
        _jwtTokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns("plain-code");
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode c, CancellationToken _) => c);
    }

    private void VerifyNoCodeIssued()
    {
        _authorizationCodeRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    // --- Step-up re-authentication -----------------------------------------

    [Fact]
    public async Task Handle_PromptLogin_WithFreshValidSession_RedirectsToLoginWithoutIssuingCode()
    {
        // Arrange — a perfectly valid, brand-new session; prompt=login must still
        // force a fresh interactive authentication.
        SetupApplication();
        SetupValidSession();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login?returnTo=");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_UnknownPromptValue_IsIgnoredAndIssuesCode()
    {
        // Arrange — only "login" triggers step-up; other prompt values are ignored.
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "consent"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task Handle_AppPolicyExceeded_RedirectsToLoginWithoutIssuingCode()
    {
        // Arrange — app requires re-auth within 30 minutes; the session is 60 old.
        var application = SetupApplication();
        application.LoadReauthenticationMaxAge(30);
        SetupSessionAged(TimeSpan.FromMinutes(60));

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_AppPolicyNotExceeded_IssuesCode()
    {
        // Arrange — app policy 30 minutes; the session is only 5 minutes old.
        var application = SetupApplication();
        application.LoadReauthenticationMaxAge(30);
        SetupSessionAged(TimeSpan.FromMinutes(5));
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task Handle_RequestMaxAgeExceeded_RedirectsToLoginWithoutIssuingCode()
    {
        // Arrange — no app policy, but the request demands a session younger than
        // 60 seconds; the session is 5 minutes old.
        SetupApplication();
        SetupSessionAged(TimeSpan.FromMinutes(5));

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", maxAge: "60"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_RequestMaxAgeSatisfied_IssuesCode()
    {
        // Arrange — request allows sessions up to 1 hour; the session is 5 min old.
        SetupApplication();
        SetupSessionAged(TimeSpan.FromMinutes(5));
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", maxAge: "3600"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task Handle_MalformedMaxAge_IsIgnoredAndIssuesCode()
    {
        // Arrange — a non-numeric max_age is treated as absent, not a failure.
        SetupApplication();
        SetupSessionAged(TimeSpan.FromMinutes(5));
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", maxAge: "not-a-number"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task Handle_RequestMaxAgeMoreRestrictiveThanAppPolicy_RedirectsToLogin()
    {
        // Arrange — app policy is a lenient 60 minutes, but the request insists on
        // 60 seconds; the more restrictive of the two wins. Session is 5 min old.
        var application = SetupApplication();
        application.LoadReauthenticationMaxAge(60);
        SetupSessionAged(TimeSpan.FromMinutes(5));

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", maxAge: "60"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }
}
