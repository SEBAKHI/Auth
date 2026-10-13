using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Authentication.OidcUserInfo;
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
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();

    // The REAL ticket service, not a mock: step-up now turns on a signed value
    // surviving a round trip, and a mock that simply agrees would test nothing.
    private readonly IStepUpTicketService _stepUpTicketService;
    private readonly IdentityProviderSettings _idpSettings = new()
    {
        AccountsBaseUrl = "https://accounts.example.com",
        // These tests pin the returnTo redirect; handles have their own suite (OI-145).
        AuthorizationRequestHandlesEnabled = false
    };

    // The system time unless a test walks a timeline (OI-150).
    private readonly AdjustableTimeProvider _clock = new();

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
            new Mock<IAuthorizationRequestRepository>(MockBehavior.Strict).Object,
            _userRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _stepUpTicketService,
            _organizationRepositoryMock.Object,
            new Auth.Application.Common.OrganizationCreatorRoleCheck(
                _roleRepositoryMock.Object, _permissionRepositoryMock.Object),
            TestHelpers.CreateOptions(_idpSettings),
            _clock,
            new Mock<ILogger<AuthorizeCommandHandler>>().Object);
    }

    /// <summary>
    /// A ticket as the server would have written it on the trip that demanded
    /// step-up, <paramref name="secondsAgo"/> in the past, for the request whose
    /// challenge is <paramref name="codeChallenge"/> (by default, every command's).
    /// </summary>
    private string IssueTicket(int secondsAgo = 2, string clientId = ClientId, string? codeChallenge = null)
        => _stepUpTicketService.Issue(clientId, codeChallenge ?? ValidChallenge, _clock.UtcNow.AddSeconds(-secondsAgo));

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
        string? stepUpTicket = null,
        string? createOrganization = null)
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
            stepUpTicket,
            CreateOrganization: createOrganization);
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
        => SetupSessionCreatedAt(_clock.UtcNow - age, idpToken);

    /// <summary>
    /// Registers a valid IdP session minted at <paramref name="createdAt"/>: the
    /// moment its sign-in finished.
    /// </summary>
    private Guid SetupSessionCreatedAt(DateTime createdAt, string idpToken = "idp-token")
    {
        var userId = Guid.NewGuid();
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

    [Theory]
    [InlineData(5, 15)]
    [InlineData(100, null)]
    public async Task Handle_AutomaticallyLockedUser_IssuesCode(int failedLoginAttempts, int? lockedForMinutes)
    {
        // X12, LK2.5: a live SSO session renews under a lock raised by wrong
        // passwords (timed or hard); only an administrator's lock sends the user
        // to sign in again (the case above).
        SetupApplication();
        var userId = SetupValidSession();
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(
                id: userId,
                status: UserStatus.Locked,
                failedLoginAttempts: failedLoginAttempts,
                lockoutEnd: lockedForMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null));
        SetupCodeIssuance();

        var result = await _handler.Handle(CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
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
    public async Task Handle_RealGeneratedCode_TravelsInTheRedirectVerbatim()
    {
        // OI-142: a firewall that decodes before it matches refuses a code in standard
        // base64, which always ended in "==": percent-encoding it as "%3D%3D" does not
        // help. A code from the real generator needs no
        // percent-encoding at all, so the redirect carries exactly the issued value.
        using var tokens = new UserInfoTokens();
        var plainCode = tokens.Service.GenerateRefreshToken();

        SetupApplication();
        SetupValidSession();
        _jwtTokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns(plainCode);
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash(plainCode)).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode c, CancellationToken _) => c);

        var result = await _handler.Handle(CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var query = new Uri(result.Value.RedirectUrl).Query;
        query.Should().StartWith($"?code={plainCode}&state=");
        query.Should().NotContain("%");
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

    #region Scopes (OI-58)

    /// <summary>The grant on the one code the handler stored.</summary>
    private string? IssuedScope() =>
        _authorizationCodeRepositoryMock.Invocations
            .Where(i => i.Method.Name == nameof(IAuthorizationCodeRepository.CreateAsync))
            .Select(i => ((AuthorizationCode)i.Arguments[0]).Scope)
            .Single();

    [Theory]
    [InlineData("openid foo")]
    [InlineData("phone_number")]
    [InlineData("OpenID")]
    [InlineData("openid\tprofile")]
    [InlineData("openid \"phone\"")]
    public async Task Handle_InvalidScope_RedirectsWithInvalidScopeAndStateAndIssuesNoCode(string scope)
    {
        // Arrange — a signed-in user, so only the scope stands between the
        // request and a code.
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = scope }, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_scope&state=xyz");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_OverlongScope_RedirectsWithInvalidScope()
    {
        SetupApplication();
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = new string('a', 513) },
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_scope&state=xyz");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task Handle_UnknownClientWithBadScope_StillReturnsInvalidClientWithoutRedirect()
    {
        // An unvalidated client must never receive an error redirect, whatever
        // else is wrong with the request.
        var result = await _handler.Handle(
            CreateCommand() with { Scope = "openid foo" }, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task Handle_UnregisteredRedirectUriWithBadScope_StillReturnsInvalidRedirectUriWithoutRedirect()
    {
        SetupApplication();

        var result = await _handler.Handle(
            CreateCommand(redirectUri: "https://evil.example.com/cb") with { Scope = "openid foo" },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidRedirectUri);
    }

    [Fact]
    public async Task Handle_BadScopeWithoutSession_FailsBeforeTheLoginPage()
    {
        // Without this order the user would sign in only to be refused on return.
        SetupApplication();

        var result = await _handler.Handle(
            CreateCommand() with { Scope = "openid foo" }, CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_scope&state=xyz");
    }

    [Fact]
    public async Task Handle_BadScopeWithASessionCookie_NeverLooksTheSessionUp()
    {
        // The browser carries a live SSO cookie, so the lookup WOULD run if the
        // scope check came after it: the check must decide first, on the request
        // alone.
        SetupApplication();
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = "openid foo" }, CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_scope&state=xyz");
        _idpSessionRepositoryMock.Verify(
            r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RequestedScopes_AreIntersectedWithTheApplicationsAllowedScopes()
    {
        // Arrange — allowed {profile, email}; phone is asked for and dropped.
        var application = SetupApplication();
        application.LoadAllowedScopes("profile email");
        SetupValidSession();
        SetupCodeIssuance();

        // Act
        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = "openid profile email phone" },
            CancellationToken.None);

        // Assert — the sign-in works; the grant is what was both asked and allowed.
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        IssuedScope().Should().Be("openid profile email");
    }

    [Fact]
    public async Task Handle_NoScopeParameter_GrantsOpenIdOnly()
    {
        var application = SetupApplication();
        application.LoadAllowedScopes("profile email phone");
        SetupValidSession();
        SetupCodeIssuance();

        await _handler.Handle(CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        IssuedScope().Should().Be("openid");
    }

    [Fact]
    public async Task Handle_ApplicationWithNoAllowedScopes_GrantsOpenIdOnly()
    {
        // NULL AllowedScopes (every application before an administrator ticks a box).
        var application = SetupApplication();
        application.LoadAllowedScopes(null);
        SetupValidSession();
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = "phone" }, CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        IssuedScope().Should().Be("openid");
    }

    [Fact]
    public async Task Handle_ScopeWithoutOpenId_StillGrantsOpenId()
    {
        var application = SetupApplication();
        application.LoadAllowedScopes("profile");
        SetupValidSession();
        SetupCodeIssuance();

        await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = "profile" }, CancellationToken.None);

        IssuedScope().Should().Be("openid profile");
    }

    [Fact]
    public async Task Handle_OfflineAccess_IsAcceptedAndNotGranted()
    {
        var application = SetupApplication();
        application.LoadAllowedScopes("email");
        SetupValidSession();
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token") with { Scope = "openid email offline_access" },
            CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        IssuedScope().Should().Be("openid email");
    }

    #endregion

    #region Organization creation from the application (OI-63): create_organization and prompt=create

    private static readonly string CreateOrganizationPage =
        $"https://accounts.example.com/create-organization?returnTo={Uri.EscapeDataString(OriginalUrl)}";

    /// <summary>
    /// Allows organization creation on the application with a usable creator
    /// role: this application's, active, carrying one permission.
    /// </summary>
    private Guid SetupCreatorRole(
        Auth.Domain.Entities.Application application,
        bool roleActive = true,
        Guid? roleApplicationId = null,
        int permissionCount = 1)
    {
        var roleId = Guid.NewGuid();
        application.LoadOrganizationCreation(true, roleId);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(
                id: roleId, applicationId: roleApplicationId ?? application.Id, isActive: roleActive));
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, permissionCount)
                .Select(i => TestHelpers.CreatePermission(code: $"shop:perm{i}"))
                .ToList());

        return roleId;
    }

    private void SetupSetUpOrganization(Guid userId, Guid? organizationId)
    {
        _organizationRepositoryMock
            .Setup(r => r.FindOrganizationSetUpForApplicationAsync(
                userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationId);
    }

    /// <summary>Each way an application can fail to offer the step.</summary>
    public static TheoryData<string> UnavailableCreationCases => new()
    {
        "not allowed", "restricted", "role inactive", "role of another application", "role without permissions",
        "role deleted",
    };

    [Fact]
    public void UnavailableCreationCases_AreNotEmpty() =>
        UnavailableCreationCases.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(UnavailableCreationCases))]
    public async Task CreateOrganization_ApplicationNotOffering_RedirectsUnauthorizedClient_BeforeAnySession(string unavailable)
    {
        var application = SetupApplication();
        switch (unavailable)
        {
            case "not allowed":
                break;
            case "restricted":
                SetupCreatorRole(application);
                application.Update(application.Name, null, null, null, null, null, false, false, false, 60, 5,
                    ApplicationAccessMode.Restricted, Guid.NewGuid());
                break;
            case "role inactive":
                SetupCreatorRole(application, roleActive: false);
                break;
            case "role of another application":
                SetupCreatorRole(application, roleApplicationId: Guid.NewGuid());
                break;
            case "role without permissions":
                SetupCreatorRole(application, permissionCount: 0);
                break;
            case "role deleted":
                application.LoadOrganizationCreation(true, Guid.NewGuid());
                break;
        }

        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", createOrganization: "true"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=unauthorized_client&state=xyz");
        _idpSessionRepositoryMock.Verify(
            r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoCodeIssued();
    }

    public static TheoryData<string> MalformedCreateOrganizationValues => new() { "false", "TRUE", "1", "", "true " };

    [Fact]
    public void MalformedCreateOrganizationValues_AreNotEmpty() =>
        MalformedCreateOrganizationValues.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(MalformedCreateOrganizationValues))]
    public async Task CreateOrganization_AnyValueButTrue_RedirectsInvalidRequest(string value)
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", createOrganization: value), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_request&state=xyz");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task CreateOrganization_SessionWithoutSetUpOrganization_RedirectsToTheCreationPageWithTheExactAuthorizeUrl()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        var userId = SetupValidSession();
        SetupSetUpOrganization(userId, null);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", createOrganization: "true"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(CreateOrganizationPage);
        result.Value.IsLoginRedirect.Should().BeFalse();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task CreateOrganization_PromptNoneWithoutSetUpOrganization_RedirectsInteractionRequired()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        var userId = SetupValidSession();
        SetupSetUpOrganization(userId, null);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "none", createOrganization: "true"),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=interaction_required&state=xyz");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task CreateOrganization_SetUpOrganization_IssuesTheCode_AskingWithTheCreatorRole()
    {
        var application = SetupApplication();
        var roleId = SetupCreatorRole(application);
        var userId = SetupValidSession();
        SetupSetUpOrganization(userId, Guid.NewGuid());
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", createOrganization: "true"), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        _organizationRepositoryMock.Verify(
            r => r.FindOrganizationSetUpForApplicationAsync(userId, application.Id, roleId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrganization_UnentitledUser_IsRefusedBeforeTheCreationPage()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupValidSession();
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", createOrganization: "true"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=access_denied&state=xyz");
        _organizationRepositoryMock.Verify(
            r => r.FindOrganizationSetUpForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WithoutCreateOrganization_TheOrganizationIsNeverConsulted()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupValidSession();
        SetupCodeIssuance();

        var result = await _handler.Handle(CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        _organizationRepositoryMock.Verify(
            r => r.FindOrganizationSetUpForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateOrganization_WithoutSession_GoesToSignInFirst()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);

        var result = await _handler.Handle(CreateCommand(createOrganization: "true"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(
            $"https://accounts.example.com/login?returnTo={Uri.EscapeDataString(OriginalUrl)}");
    }

    [Fact]
    public async Task PromptCreate_WithoutSession_OpensRegistrationWithTheAuthorizeUrlAsReturnTo()
    {
        SetupApplication();

        var result = await _handler.Handle(CreateCommand(prompt: "create"), CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.RedirectUrl.Should().Be(
            $"https://accounts.example.com/register?returnTo={Uri.EscapeDataString(OriginalUrl)}");
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task PromptCreate_WithSession_ChangesNothing()
    {
        SetupApplication();
        SetupValidSession();
        SetupCodeIssuance();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "create"), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
    }

    [Fact]
    public async Task PromptNoneWithCreate_IsInvalidRequest()
    {
        SetupApplication();

        var result = await _handler.Handle(CreateCommand(prompt: "none create"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=invalid_request&state=xyz");
    }

    [Fact]
    public async Task PromptCreate_WithStepUpDemanded_OpensRegistrationAndRecordsTheDemand()
    {
        // OI-150 inverts what this test pinned since OI-63 ("a session that must
        // re-authenticate belongs to an existing account"). A client that sends
        // create with login asks who is at the keyboard, and offers sign-up; the
        // registration page keeps its sign-in link with the same request, so an
        // existing account is one click away.
        SetupApplication();
        SetupValidSession();

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login create"), CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.RedirectUrl.Should().StartWith(RegisterPage);
        result.Value.StepUpTicketToSet.Should().NotBeNull("the sign-up has to answer the demand it was sent for");
        VerifyNoCodeIssued();
    }

    #endregion

    #region OI-150: prompt=login asks once, "create login" opens registration, a ticket answers only its own request

    private const string LoginPage = "https://accounts.example.com/login?returnTo=";
    private const string RegisterPage = "https://accounts.example.com/register?returnTo=";

    // Two requests of the same client carry two challenges: a conformant client
    // makes a new one for every request.
    private static readonly string FirstChallenge = new('1', 43);
    private static readonly string SecondChallenge = new('2', 43);

    [Fact]
    public async Task PromptLogin_WithoutSession_RecordsTheDemand_SoOneSignInAnswersIt()
    {
        // I5. The sessionless trip wrote no ticket, so the return after the
        // sign-in found no proof and sent the person to sign in a second time.
        SetupApplication();
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var request = CreateCommand(prompt: "login");

        var first = await _handler.Handle(request, CancellationToken.None);

        first.Value.IsLoginRedirect.Should().BeTrue();
        first.Value.RedirectUrl.Should().StartWith(LoginPage);
        var ticket = first.Value.StepUpTicketToSet;
        ticket.Should().NotBeNull("the sign-in it leads to must be able to answer it");
        VerifyNoCodeIssued();

        // The person signs in, which mints the session, and the browser comes
        // back to the same request carrying the cookie.
        _clock.Advance(TimeSpan.FromSeconds(40));
        SetupSessionCreatedAt(_clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(2));

        var second = await _handler.Handle(
            request with { IdpSessionToken = "idp-token", StepUpTicket = ticket }, CancellationToken.None);

        second.Value.IsLoginRedirect.Should().BeFalse("one sign-in answers the demand");
        second.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        second.Value.ClearStepUpTicket.Should().BeTrue();
    }

    public static TheoryData<string> SessionsThatCannotRenew => new() { "user missing", "locked by an administrator" };

    [Fact]
    public void SessionsThatCannotRenew_AreNotEmpty() => SessionsThatCannotRenew.Count.Should().BeGreaterThan(0);

    /// <summary>A valid session whose user is gone, or may no longer sign in.</summary>
    private void SetupSessionThatCannotRenew(string why)
    {
        var userId = SetupValidSession();
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(why == "user missing" ? null : TestHelpers.CreateUser(id: userId, status: UserStatus.Locked));
    }

    [Theory]
    [MemberData(nameof(SessionsThatCannotRenew))]
    public async Task PromptLogin_SessionThatCannotRenew_RedirectsToLoginAndRecordsTheDemand(string why)
    {
        SetupApplication();
        SetupSessionThatCannotRenew(why);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login"), CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.RedirectUrl.Should().StartWith(LoginPage);
        result.Value.StepUpTicketToSet.Should().NotBeNull();
        VerifyNoCodeIssued();
    }

    [Theory]
    [MemberData(nameof(SessionsThatCannotRenew))]
    public async Task PromptCreateLogin_SessionThatCannotRenew_OpensRegistrationAndRecordsTheDemand(string why)
    {
        SetupApplication();
        SetupSessionThatCannotRenew(why);

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "create login"), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RegisterPage);
        result.Value.StepUpTicketToSet.Should().NotBeNull();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task PromptCreateLogin_WithoutSession_OpensRegistration_AndTheSignUpAnswersTheDemand()
    {
        SetupApplication();
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var request = CreateCommand(prompt: "create login");

        var first = await _handler.Handle(request, CancellationToken.None);

        first.Value.RedirectUrl.Should().StartWith(RegisterPage);
        first.Value.StepUpTicketToSet.Should().NotBeNull();

        // A registration with an e-mailed code takes minutes, inside the ticket's life.
        _clock.Advance(TimeSpan.FromMinutes(12));
        SetupSessionCreatedAt(_clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(2));

        var second = await _handler.Handle(
            request with { IdpSessionToken = "idp-token", StepUpTicket = first.Value.StepUpTicketToSet },
            CancellationToken.None);

        second.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        second.Value.ClearStepUpTicket.Should().BeTrue();
    }

    [Fact]
    public async Task PromptCreateLogin_WithCreateOrganization_PassesTheOrganizationStep_AndTheSameRequestGetsTheCode()
    {
        // T3, and T7 through the URL (this suite runs with handles off): the
        // request comes back after the organization step with the same challenge,
        // so its ticket still answers it, and is spent with the code.
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var request = CreateCommand(codeChallenge: FirstChallenge, prompt: "create login", createOrganization: "true");

        var first = await _handler.Handle(request, CancellationToken.None);
        first.Value.RedirectUrl.Should().StartWith(RegisterPage);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var userId = SetupSessionCreatedAt(_clock.UtcNow);
        SetupSetUpOrganization(userId, null);
        _clock.Advance(TimeSpan.FromSeconds(2));
        var signedIn = request with { IdpSessionToken = "idp-token", StepUpTicket = first.Value.StepUpTicketToSet };

        var toOrganization = await _handler.Handle(signedIn, CancellationToken.None);

        toOrganization.Value.RedirectUrl.Should().Be(CreateOrganizationPage);
        toOrganization.Value.StepUpTicketToSet.Should().BeNull("the demand is answered; nothing new is demanded");
        toOrganization.Value.ClearStepUpTicket.Should().BeFalse("the request still has to come back for its code");
        VerifyNoCodeIssued();

        // The organization step, inside the finish window, then the page sends
        // the browser back to the same request.
        _clock.Advance(TimeSpan.FromMinutes(2));
        SetupSetUpOrganization(userId, Guid.NewGuid());

        var resumed = await _handler.Handle(signedIn, CancellationToken.None);

        resumed.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        resumed.Value.ClearStepUpTicket.Should().BeTrue("one demand, one code: the ticket is spent");
    }

    public static TheoryData<string?> PromptsWithoutLogin => new() { null, "create", "consent" };

    [Fact]
    public void PromptsWithoutLogin_AreNotEmpty() => PromptsWithoutLogin.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(PromptsWithoutLogin))]
    public async Task WithoutPromptLogin_NoSession_WritesNoTicket(string? prompt)
    {
        SetupApplication();

        var result = await _handler.Handle(CreateCommand(prompt: prompt), CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeTrue();
        result.Value.StepUpTicketToSet.Should().BeNull("nothing was demanded");
    }

    [Theory]
    [InlineData("none", "login_required")]
    [InlineData("none login", "invalid_request")]
    [InlineData("none create", "invalid_request")]
    public async Task PromptNone_NoSession_AnswersTheErrorWithoutATicket(string prompt, string error)
    {
        SetupApplication();

        var result = await _handler.Handle(CreateCommand(prompt: prompt), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error={error}&state=xyz");
        result.Value.StepUpTicketToSet.Should().BeNull();
        VerifyNoCodeIssued();
    }

    [Fact]
    public async Task SharedComputer_ATicketProvedForOneRequest_DoesNotAnswerTheNextRequest()
    {
        // Gap 3 as a timeline. Person A starts registering an organization, signs
        // in, and walks away on the organization page. Person B clicks "Sign in"
        // in the same application on the same browser a minute later: a NEW
        // request, with its own challenge. Before the binding, A's ticket
        // answered it, and B was issued a code for A's account with no password.
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);

        // t0: A's request R1 records its demand.
        var r1 = CreateCommand(codeChallenge: FirstChallenge, prompt: "create login", createOrganization: "true");
        var cookie = (await _handler.Handle(r1, CancellationToken.None)).Value.StepUpTicketToSet!;

        // t1: A signs in. R1 is proved and goes to the organization page, its
        // ticket unspent: the request still has to come back for its code.
        _clock.Advance(TimeSpan.FromMinutes(1));
        var userA = SetupSessionCreatedAt(_clock.UtcNow);
        SetupSetUpOrganization(userA, null);
        var proved = await _handler.Handle(
            r1 with { IdpSessionToken = "idp-token", StepUpTicket = cookie }, CancellationToken.None);
        proved.Value.RedirectUrl.Should().Be(CreateOrganizationPage);
        proved.Value.ClearStepUpTicket.Should().BeFalse();

        // t3: B's request R2, inside A's finish window and the ticket's lifetime,
        // carrying A's session and A's cookie.
        _clock.Advance(TimeSpan.FromMinutes(2));
        var r2 = CreateCommand(
            codeChallenge: SecondChallenge, prompt: "login", idpSessionToken: "idp-token", stepUpTicket: cookie);

        var result = await _handler.Handle(r2, CancellationToken.None);

        result.Value.IsLoginRedirect.Should().BeTrue("B is asked for a password");
        result.Value.RedirectUrl.Should().StartWith(LoginPage);
        result.Value.StepUpTicketToSet.Should().NotBeNull().And.NotBe(cookie, "R2 raises a demand of its own");
        VerifyNoCodeIssued();
    }

    [Fact]
    public void StepUpTicketLifetime_DefaultsToThirtyMinutes()
    {
        var defaults = new IdentityProviderSettings();

        defaults.StepUpTicketLifetimeMinutes.Should().Be(30);
        defaults.StepUpTicketLifetime.Should().Be(TimeSpan.FromMinutes(30));
        _idpSettings.StepUpTicketLifetime.Should().Be(TimeSpan.FromMinutes(30), "this suite runs on the default");
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public async Task PromptLogin_ATicketProvesForThirtyMinutes(int ticketMinutesOld, bool proved)
    {
        SetupApplication();
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var ticket = IssueTicket(secondsAgo: ticketMinutesOld * 60);
        // Signed in after the demand, and inside the finish window.
        SetupSessionAged(TimeSpan.FromMinutes(1));

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: ticket), CancellationToken.None);

        result.Value.IsLoginRedirect.Should().Be(!proved);
        result.Value.RedirectUrl.StartsWith($"{RedirectUri}?code=", StringComparison.Ordinal).Should().Be(proved);
    }

    [Fact]
    public void StepUpFinishWindow_DefaultsToFiveMinutes()
    {
        new IdentityProviderSettings().StepUpFinishWindowMinutes.Should().Be(5);
        _idpSettings.StepUpFinishWindow.Should().Be(TimeSpan.FromMinutes(5), "this suite runs on the default");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(61, 60)]
    [InlineData(1000, 60)]
    public void StepUpClocks_AreHeldToOneToSixtyMinutes(int configured, int expected)
    {
        var settings = new IdentityProviderSettings
        {
            StepUpTicketLifetimeMinutes = configured,
            StepUpFinishWindowMinutes = configured
        };

        settings.StepUpTicketLifetime.Should().Be(TimeSpan.FromMinutes(expected));
        settings.StepUpFinishWindow.Should().Be(TimeSpan.FromMinutes(expected));
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(301, false)]
    public async Task PromptLogin_TheProofHoldsOnlyInsideTheFinishWindow(int secondsSinceSignIn, bool proved)
    {
        // 4:59 after the sign-in it still answers; 5:01 after, the person signs in
        // again, with a fresh demand.
        SetupApplication();
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var ticket = IssueTicket(secondsAgo: 10 * 60);
        SetupSessionAged(TimeSpan.FromSeconds(secondsSinceSignIn));

        var result = await _handler.Handle(
            CreateCommand(idpSessionToken: "idp-token", prompt: "login", stepUpTicket: ticket), CancellationToken.None);

        if (proved)
        {
            result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
            result.Value.ClearStepUpTicket.Should().BeTrue();
        }
        else
        {
            result.Value.IsLoginRedirect.Should().BeTrue();
            result.Value.RedirectUrl.Should().StartWith(LoginPage);
            result.Value.StepUpTicketToSet.Should().NotBeNull("a fresh demand");
            VerifyNoCodeIssued();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(6 * 24 * 60)]
    public async Task WithoutPromptLogin_ASessionOfAnyAge_StillGetsACode(int minutesSinceSignIn)
    {
        // The finish window applies only where login was demanded: plain single
        // sign-on is untouched.
        SetupApplication();
        SetupCodeIssuance();
        SetupSessionAged(TimeSpan.FromMinutes(minutesSinceSignIn));

        var result = await _handler.Handle(CreateCommand(idpSessionToken: "idp-token"), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=plain-code");
        result.Value.StepUpTicketToSet.Should().BeNull();
    }

    [Fact]
    public async Task BackButton_TheSameRequestSixMinutesAfterTheSignIn_AsksForThePasswordAgain()
    {
        // Gap 4's table. 10:00 A starts registering an organization; 10:01 A signs
        // in and reaches the organization page; 10:02 A leaves. 10:07 someone
        // presses Back or reloads, and the page comes back to the same request,
        // whose organization is set up by now: without the finish window, A's
        // code went to whoever was at the keyboard. Now the person authenticates
        // again: on the registration page, since the request carries create, with
        // its sign-in link.
        var application = SetupApplication();
        SetupCreatorRole(application);
        SetupCodeIssuance();
        _clock.Advance(TimeSpan.Zero);
        var r1 = CreateCommand(prompt: "create login", createOrganization: "true");
        var cookie = (await _handler.Handle(r1, CancellationToken.None)).Value.StepUpTicketToSet!;

        _clock.Advance(TimeSpan.FromMinutes(1));
        var userA = SetupSessionCreatedAt(_clock.UtcNow);
        SetupSetUpOrganization(userA, null);
        var signedIn = r1 with { IdpSessionToken = "idp-token", StepUpTicket = cookie };
        (await _handler.Handle(signedIn, CancellationToken.None)).Value.RedirectUrl.Should().Be(CreateOrganizationPage);

        _clock.Advance(TimeSpan.FromMinutes(6));
        SetupSetUpOrganization(userA, Guid.NewGuid());

        var back = await _handler.Handle(signedIn, CancellationToken.None);

        back.Value.IsLoginRedirect.Should().BeTrue();
        back.Value.RedirectUrl.Should().StartWith(RegisterPage);
        back.Value.StepUpTicketToSet.Should().NotBeNull("a fresh demand, answered by a fresh sign-in");
        VerifyNoCodeIssued();
    }

    #endregion
}
