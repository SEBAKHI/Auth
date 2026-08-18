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
    private readonly Mock<IApplicationAccessRepository> _applicationAccessRepositoryMock = new();
    private readonly Mock<IIdpSessionRepository> _idpSessionRepositoryMock = new();
    private readonly Mock<IAuthorizationCodeRepository> _authorizationCodeRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock = new();
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock = new();

    // The REAL ticket service, not a mock: step-up now turns on a signed value
    // surviving a round trip, and a mock that simply agrees would test nothing.
    private readonly IStepUpTicketService _stepUpTicketService;
    private readonly IdentityProviderSettings _idpSettings = new()
    {
        AccountsBaseUrl = "https://accounts.example.com"
    };

    private readonly AuthorizeCommandHandler _handler;

    public AuthorizeCommandHandlerTests()
    {
        // Entitled by default: every test that is not about the gate would
        // otherwise have to say something about invitations to say nothing.
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // A deterministic stand-in for the HMAC, base64 like the real one so it
        // shares the property the ticket format relies on: a signature never
        // contains the field separator. Declared first so the specific token
        // setups in the helpers below still win.
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string value) =>
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"hash:{value}")));

        _stepUpTicketService = new Auth.Infrastructure.Authentication.StepUpTicketService(
            _refreshTokenKeyServiceMock.Object);

        _handler = new AuthorizeCommandHandler(
            _applicationRepositoryMock.Object,
            _applicationAccessRepositoryMock.Object,
            _idpSessionRepositoryMock.Object,
            _authorizationCodeRepositoryMock.Object,
            _userRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _stepUpTicketService,
            TestHelpers.CreateOptions(_idpSettings),
            new Mock<ILogger<AuthorizeCommandHandler>>().Object);
    }

    /// <summary>
    /// A ticket as the server would have written it on the trip that demanded
    /// step-up, <paramref name="secondsAgo"/> in the past.
    /// </summary>
    private string IssueTicket(int secondsAgo = 2, string clientId = ClientId)
        => _stepUpTicketService.Issue(clientId, DateTime.UtcNow.AddSeconds(-secondsAgo));

    private static AuthorizeCommand CreateCommand(
        string? responseType = "code",
        string? clientId = ClientId,
        string? redirectUri = RedirectUri,
        string? codeChallenge = null,
        string? codeChallengeMethod = "S256",
        string? state = "xyz",
        string? idpSessionToken = null,
        string? prompt = null,
        string? maxAge = null,
        string? stepUpTicket = null)
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
            maxAge,
            stepUpTicket);
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
        result.Value.StepUpTicketToSet.Should().NotBeNull("the demand has to be recorded to be provable later");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLogin_StrippingTheParameterNoLongerBypassesStepUp()
    {
        // THE defect this phase closes. The demand used to be satisfied by its own
        // removal, so anyone holding a live session cookie could delete prompt=login
        // from the address bar and be issued a code against the stale session —
        // precisely the person step-up exists to stop. Now the parameter's absence
        // buys nothing on its own: without a fresh session there is nothing to
        // prove, and the request is answered on its merits.
        SetupApplication();
        SetupSessionAged(TimeSpan.FromDays(3));
        SetupCodeIssuance();

        // The attacker's second request: parameter removed, same stale session.
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: null, maxAge: "300"),
            CancellationToken.None);

        // max_age is likewise no longer strippable in practice, because the server
        // now keeps its own record and the SPA no longer has to delete anything.
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLogin_WithTicketButStaleSession_StillDemandsStepUp()
    {
        // Holding a ticket is not enough: it proves a demand was MADE, and only a
        // session minted after it proves one was ANSWERED.
        SetupApplication();
        SetupSessionAged(TimeSpan.FromHours(2));

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: IssueTicket()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLogin_WithTicketAndFreshlyMintedSession_IssuesCodeAndSpendsTheTicket()
    {
        // The honest round trip: demand recorded, user signs in, the new session
        // postdates the demand.
        SetupApplication();
        var ticket = IssueTicket(secondsAgo: 30);
        SetupSessionAged(TimeSpan.Zero);
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: ticket),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith(RedirectUri);
        result.Value.ClearStepUpTicket.Should().BeTrue(
            "one re-authentication must answer one demand, not every demand in the ticket's lifetime");
    }

    [Fact]
    public async Task Handle_PromptLogin_TicketIssuedForAnotherClient_IsRejected()
    {
        // Otherwise a client the user merely has access to could mint tickets that
        // satisfy a sensitive client's re-authentication.
        SetupApplication();
        var foreignTicket = IssueTicket(secondsAgo: 30, clientId: "OTHER_APP");
        SetupSessionAged(TimeSpan.Zero);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: foreignTicket),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Theory]
    [InlineData("not-a-ticket")]
    [InlineData("9999999999|CRM|forged-signature")]
    [InlineData("|CRM|")]
    [InlineData("")]
    public async Task Handle_PromptLogin_TamperedTicket_IsRejected(string ticket)
    {
        SetupApplication();
        SetupSessionAged(TimeSpan.Zero);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: ticket),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue("an unreadable ticket must never satisfy a demand");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLogin_ExpiredTicket_IsRejected()
    {
        SetupApplication();
        var staleTicket = IssueTicket(secondsAgo: (int)_idpSettings.StepUpTicketLifetime.TotalSeconds + 60);
        SetupSessionAged(TimeSpan.Zero);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: staleTicket),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLoginSatisfied_StillHonoursMaxAge()
    {
        // Answering prompt=login must not waive the other freshness rule; when a
        // client asks for both, the stricter wins.
        SetupApplication();
        var ticket = IssueTicket(secondsAgo: 30);
        SetupSessionAged(TimeSpan.FromMinutes(20));

        var result = await _handler.Handle(
            CreateCommand(
                idpSessionToken: "idp-token", prompt: "login", maxAge: "60", stepUpTicket: ticket),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptLogin_IsMatchedCaseInsensitively()
    {
        SetupApplication();
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "Login"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue("case must not be a way to skip the demand");
        VerifyNoCodeIssued();
    }

    // --- prompt=none: no UI may be shown ------------------------------------

    [Fact]
    public async Task Handle_PromptNone_WithoutSession_ReturnsLoginRequiredInsteadOfRedirectingToLogin()
    {
        // What a silent-renewal iframe needs. It used to receive a 302 to the login
        // page and render it inside the frame.
        SetupApplication();

        var result = await _handler.Handle(
            CreateCommand(prompt: "none"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith(RedirectUri);
        result.Value.RedirectUrl.Should().Contain("error=login_required");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptNone_WithValidSession_IssuesCode()
    {
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "none"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Contain("code=");
    }

    [Fact]
    public async Task Handle_PromptNone_WhenStepUpWouldBeRequired_ReturnsLoginRequired()
    {
        SetupApplication();
        SetupSessionAged(TimeSpan.FromHours(2));

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "none", maxAge: "60"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Contain("error=login_required");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_PromptNoneCombinedWithAnotherValue_IsInvalidRequest()
    {
        // OIDC Core §3.1.2.1: "none" may not be combined with any other value.
        SetupApplication();
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "none login"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Contain("error=invalid_request");
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

    #region Entitlement gate

    /// <summary>Makes the gate refuse whoever asks.</summary>
    private void DenyEntitlement()
    {
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task Handle_EntitledUser_IssuesCode()
    {
        // Arrange
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task Handle_UnentitledUser_RedirectsWithAccessDeniedAndIssuesNoCode()
    {
        // Arrange
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();
        DenyEntitlement();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert — refused through the redirect, not as a bare error: the client
        // must be able to tell "not signed in" from "signed in, not allowed".
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith(RedirectUri);
        result.Value.RedirectUrl.Should().Contain("error=access_denied");
        result.Value.RedirectUrl.Should().Contain("state=xyz");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_UnentitledUser_ErrorRedirectCarriesNoDescription()
    {
        // Arrange
        SetupApplication();
        SetupValidSession();
        DenyEntitlement();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert — the client learns it was refused, never why or about whom.
        result.Value.RedirectUrl.Should().NotContain("error_description");
    }

    [Fact]
    public async Task Handle_UnentitledUser_NeverRedirectsAnywhereButTheRegisteredUri()
    {
        // Arrange
        SetupApplication();
        SetupValidSession();
        DenyEntitlement();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert
        result.Value.RedirectUrl.Should().StartWith(RedirectUri);
        result.Value.IsLoginRedirect.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_StepUpRequired_DoesNotConsultTheGate()
    {
        // Arrange — a stale session with a step-up policy bounces to login first.
        // Checking entitlement earlier would let a stolen stale cookie enumerate
        // which applications its owner may enter, without knowing the password.
        var application = SetupApplication();
        application.LoadReauthenticationMaxAge(1);
        SetupSessionAged(TimeSpan.FromMinutes(30));
        DenyEntitlement();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert
        result.Value.IsLoginRedirect.Should().BeTrue();
        _applicationAccessRepositoryMock.Verify(
            r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveApplication_IsRejectedBeforeTheGateIsConsulted()
    {
        // Arrange — the on/off switch beats the access mode, and beats it early:
        // an unknown client and a switched-off one must be indistinguishable.
        SetupApplication(isActive: false);
        SetupValidSession();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidClient);
        _applicationAccessRepositoryMock.Verify(
            r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
