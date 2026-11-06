using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S23, AM3 and AM7: the authorize endpoint applies an application's two-step requirement.
/// The outcome by prompt and mode; the loop guard; the order (guard before freshness, the
/// requirement before the entitlement, ARR-0784); the session's use never recorded on a
/// redirect; the tickets erased when the request ends; <c>reason</c> in both URL shapes.
/// The authorize handler's own suite (<see cref="AuthorizeCommandHandlerTests"/>) covers
/// everything else, with the flag off.
/// </summary>
/// <remarks>
/// Deliberate breaks: move the freshness check before the loop guard (the max_age=0 guard
/// test turns red); move the entitlement before the requirement (the two ARR-0784 tests
/// turn red).
/// </remarks>
public class AuthorizeCommandHandlerAppMfaTests
{
    private const string ClientId = "CRM";
    private const string RedirectUri = "https://crm.example.com/callback";
    private const string OriginalUrl = "https://auth.example.com/api/v1/auth/authorize?response_type=code&client_id=CRM";
    private static readonly string Challenge = new('a', 43);
    private static readonly string OtherChallenge = new('b', 43);

    private static readonly AuthenticationMethods PasswordOnly = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods PasswordAndTotp = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IAuthorizationCodeRepository> _codes = new();
    private readonly Mock<IAuthorizationRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITwoFactorStateStore> _factors = new(MockBehavior.Strict);
    private readonly FakeCredentialHasher _hashes = new();
    private readonly AppMfaTicketService _tickets = TestAppMfa.Tickets();
    private readonly StepUpTicketService _stepUpTickets;
    private readonly AdjustableTimeProvider _clock = new();
    private readonly CapturingLogger<AuthorizeCommandHandler> _log = new();
    private readonly IdentityProviderSettings _settings = new()
    {
        AccountsBaseUrl = "https://accounts.example.com",
        AuthorizationRequestHandlesEnabled = false,
        ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Enforce)
    };

    private readonly AuthorizeCommandHandler _handler;
    private Guid _userId;
    private IdpSession? _session;

    public AuthorizeCommandHandlerAppMfaTests()
    {
        _hashes.MapAll(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"hash:{value}")));
        _stepUpTickets = new StepUpTicketService(_hashes);

        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns("plain-code");
        _codes.Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode c, CancellationToken _) => c);

        _handler = new AuthorizeCommandHandler(
            _applications.Object,
            _access.Object,
            TestIdpSessions.Reader(_idpSessions, _hashes, _settings, _clock),
            _codes.Object,
            _requests.Object,
            _users.Object,
            _jwt.Object,
            _hashes,
            _stepUpTickets,
            _tickets,
            TestAppMfa.Policy(_factors.Object),
            new Mock<IOrganizationRepository>().Object,
            new Auth.Application.Common.OrganizationCreatorRoleCheck(
                new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
            TestHelpers.CreateOptions(_settings),
            _clock,
            _log);
    }

    // ── arrangement ─────────────────────────────────────────────────────────

    private ApplicationEntity Application(bool requireTwoFactor = true)
    {
        var application = new ApplicationEntity(
            Guid.NewGuid(), ClientId, "CRM", null, "https://crm.example.com", null, null,
            isActive: true, allowSelfRegistration: false, requireTwoFactor: requireTwoFactor,
            requireEmailVerification: false, sessionTimeoutMinutes: 60, maxConcurrentSessions: 5,
            DateTime.UtcNow, Guid.NewGuid(), null, null, ApplicationAccessMode.Everyone);
        application.LoadRedirectUris([RedirectUri]);
        _applications.Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(application);
        return application;
    }

    private void Session(AuthenticationMethods methods, bool hasFactor = true, TimeSpan? age = null, bool hasPassword = true)
    {
        _userId = Guid.NewGuid();
        var createdAt = _clock.UtcNow - (age ?? TimeSpan.Zero);
        _session = new IdpSession(
            Guid.NewGuid(), _userId, "idp-hash", createdAt, createdAt.AddDays(7), null, null, null,
            isPersistent: true, lastActivityAt: _clock.UtcNow, authMethods: methods.ToStored());

        _hashes.Map("idp-token", "idp-hash");
        _idpSessions.Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("idp-hash"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId, passwordHash: hasPassword ? "TestPasswordHash" : null));
        _factors.Setup(s => s.GetQualifyingFactorsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualifyingSecondFactors(Totp: hasFactor, Passkey: false));
    }

    private void Mode(ApplicationMfaEnforcementMode mode) => _settings.ApplicationMfaEnforcement = mode.ToString();

    private static AuthorizeCommand Command(
        string? prompt = null, string? maxAge = null, string? appMfaTicket = null, string? stepUpTicket = null,
        string? challenge = null) =>
        new("code", ClientId, RedirectUri, challenge ?? Challenge, "S256", "xyz", "idp-token", OriginalUrl, "127.0.0.1",
            prompt, maxAge, stepUpTicket, AppMfaTicket: appMfaTicket);

    private string Ticket(ApplicationMfaRequirement reason, string? challenge = null) =>
        _tickets.Issue(ClientId, _userId, reason, challenge ?? Challenge, _clock.UtcNow.AddMinutes(-1));

    private void VerifyNoCode() =>
        _codes.Verify(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyUseNotRecorded() =>
        _idpSessions.Verify(r => r.RecordActivityAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);

    private ApplicationMfaRequirement ReasonOf(string? ticket)
    {
        _tickets.TryRead(ticket, ClientId, _userId, Challenge, _clock.UtcNow, out var reason).Should().BeTrue();
        return reason;
    }

    // ── outcome × mode ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, true, ApplicationMfaRequirement.StepUp, "mfa_step_up")]
    [InlineData(1, false, ApplicationMfaRequirement.Enroll, "mfa_enroll")]
    [InlineData(0, true, ApplicationMfaRequirement.Reauthenticate, "mfa_reauthenticate")]
    [InlineData(4, true, ApplicationMfaRequirement.Reauthenticate, "mfa_reauthenticate")]
    public async Task Handle_EnforceInteractive_RedirectsToSignInWithTheReason_AndATicket(
        int storedMethods, bool hasFactor, ApplicationMfaRequirement expected, string word)
    {
        Application();
        Session(AuthenticationMethods.FromStored(storedMethods), hasFactor);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login?returnTo=");
        result.Value.RedirectUrl.Should().EndWith($"&reason={word}&method=password");
        result.Value.IsLoginRedirect.Should().BeTrue();
        ReasonOf(result.Value.AppMfaTicketToSet).Should().Be(expected);
        result.Value.StepUpTicketToSet.Should().BeNull("the requirement raises no step-up demand");
        result.Value.ClearStepUpTicket.Should().BeFalse("the step-up ticket is spent at the code, never at a redirect");
        VerifyNoCode();
        VerifyUseNotRecorded();
    }

    [Fact]
    public async Task Handle_EnforceSatisfiedSession_IssuesTheCode()
    {
        Application();
        Session(PasswordAndTotp);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
        result.Value.AppMfaTicketToSet.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FlagOff_IssuesTheCode_AndNeverReadsTheFactor()
    {
        Application(requireTwoFactor: false);
        Session(PasswordOnly);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
        _factors.Verify(s => s.GetQualifyingFactorsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    public async Task Handle_Monitor_IssuesTheCodeAsBefore_AndWritesWouldRequire(string? prompt)
    {
        Mode(ApplicationMfaEnforcementMode.Monitor);
        Application();
        Session(PasswordOnly);

        var result = await _handler.Handle(Command(prompt: prompt), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
        result.Value.AppMfaTicketToSet.Should().BeNull();
        _log.Entries.Should().ContainSingle(entry =>
            entry.Values.Contains(AppMfaSignalCodes.WouldRequire) && entry.Values.Contains("StepUp"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Handle_EnforcePromptNone_IsLoginRequired_ForEveryOutcome(int storedMethods, bool hasFactor)
    {
        Application();
        Session(AuthenticationMethods.FromStored(storedMethods), hasFactor);

        var result = await _handler.Handle(Command(prompt: "none"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=login_required&state=xyz");
        result.Value.AppMfaTicketToSet.Should().BeNull();
        VerifyNoCode();
        VerifyUseNotRecorded();
    }

    [Fact]
    public async Task Handle_EnforcePromptNoneWithATicket_NeitherReadsNorErasesIt()
    {
        Application();
        Session(PasswordOnly);

        // The same reason a guard would refuse on, and a step-up ticket of this request: prompt=none touches neither.
        var result = await _handler.Handle(
            Command(prompt: "none", appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp),
                stepUpTicket: _stepUpTickets.Issue(ClientId, Challenge, _clock.UtcNow.AddMinutes(-1))),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=login_required&state=xyz");
        result.Value.ClearAppMfaTicket.Should().BeFalse();
        result.Value.ClearStepUpTicket.Should().BeFalse();
        _log.Entries.Should().NotContain(entry => entry.Values.Contains(AppMfaSignalCodes.GuardDenied));
    }

    // ── the loop guard (AM7 (7-4)) ──────────────────────────────────────────

    [Fact]
    public async Task Handle_BackWithoutProgress_IsAccessDenied_AndErasesBothTickets()
    {
        Application();
        Session(PasswordOnly);

        var result = await _handler.Handle(
            Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp),
                stepUpTicket: _stepUpTickets.Issue(ClientId, Challenge, _clock.UtcNow.AddMinutes(-1))),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(
            $"{RedirectUri}?error=access_denied&error_description=application_requires_two_step_verification&state=xyz");
        result.Value.ClearAppMfaTicket.Should().BeTrue();
        result.Value.ClearStepUpTicket.Should().BeTrue();
        result.Value.AppMfaTicketToSet.Should().BeNull();
        _log.Entries.Should().ContainSingle(entry => entry.Values.Contains(AppMfaSignalCodes.GuardDenied));
        VerifyNoCode();
    }

    [Fact]
    public async Task Handle_ADifferentReason_IsProgress_AndGetsANewTicket()
    {
        // Enroll last time; the authenticator is bound now, so the session is short of the code only.
        Application();
        Session(PasswordOnly, hasFactor: true);

        var result = await _handler.Handle(Command(appMfaTicket: Ticket(ApplicationMfaRequirement.Enroll)), CancellationToken.None);

        result.Value.RedirectUrl.Should().EndWith("&reason=mfa_step_up&method=password");
        ReasonOf(result.Value.AppMfaTicketToSet).Should().Be(ApplicationMfaRequirement.StepUp);
    }

    [Fact]
    public async Task Handle_ANewRequestOfTheApplication_IsARedirect_NeverARefusal()
    {
        Application();
        Session(PasswordOnly);

        // The ticket answers another request (another code_challenge): this one starts afresh.
        var result = await _handler.Handle(
            Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp, challenge: OtherChallenge)), CancellationToken.None);

        result.Value.RedirectUrl.Should().EndWith("&reason=mfa_step_up&method=password");
        ReasonOf(result.Value.AppMfaTicketToSet).Should().Be(ApplicationMfaRequirement.StepUp);
    }

    [Fact]
    public async Task Handle_TheGuard_RunsBeforeTheFreshnessCheck()
    {
        // max_age=0 asks for a sign-in on every return: without the guard first, the person
        // would loop between authorize and the sign-in page.
        Application();
        Session(PasswordOnly, age: TimeSpan.FromMinutes(5));

        var result = await _handler.Handle(
            Command(maxAge: "0", appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp)), CancellationToken.None);

        result.Value.RedirectUrl.Should().Contain("error=access_denied");
        result.Value.StepUpTicketToSet.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Monitor_NeverRefusesOnATicket()
    {
        Mode(ApplicationMfaEnforcementMode.Monitor);
        Application();
        Session(PasswordOnly);

        var result = await _handler.Handle(Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp)), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
    }

    // ── the requirement before the entitlement (ARR-0784, D-T3 A) ─────────────

    [Fact]
    public async Task Handle_UnentitledUserWithPasswordOnly_Interactive_IsRedirected_AndNeverAskedAboutEntitlement()
    {
        Application();
        Session(PasswordOnly);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.RedirectUrl.Should().EndWith("&reason=mfa_step_up&method=password");
        _access.Verify(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnentitledUserWithPasswordOnly_PromptNone_IsLoginRequired_AndNeverAskedAboutEntitlement()
    {
        Application();
        Session(PasswordOnly);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(prompt: "none"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Contain("error=login_required");
        _access.Verify(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── the request is over: its tickets go (AM3, AM7 (7-1)) ──────────────────

    [Fact]
    public async Task Handle_EntitlementRefused_ErasesBothTicketsOfThisRequest()
    {
        Application();
        Session(PasswordAndTotp);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp),
                stepUpTicket: _stepUpTickets.Issue(ClientId, Challenge, _clock.UtcNow.AddMinutes(-1))),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=access_denied&state=xyz");
        result.Value.ClearAppMfaTicket.Should().BeTrue();
        result.Value.ClearStepUpTicket.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TheCode_ErasesTheTicketsThisRequestRaised()
    {
        Application();
        Session(PasswordAndTotp);

        var result = await _handler.Handle(
            Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp),
                stepUpTicket: _stepUpTickets.Issue(ClientId, Challenge, _clock.UtcNow.AddMinutes(-1))),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
        result.Value.ClearAppMfaTicket.Should().BeTrue();
        result.Value.ClearStepUpTicket.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TheCode_LeavesAnotherRequestsTickets()
    {
        // The cookies are one per browser: another request, in another tab, may be waiting on them.
        Application();
        Session(PasswordAndTotp);

        var result = await _handler.Handle(
            Command(appMfaTicket: Ticket(ApplicationMfaRequirement.StepUp, challenge: OtherChallenge),
                stepUpTicket: _stepUpTickets.Issue(ClientId, OtherChallenge, _clock.UtcNow.AddMinutes(-1))),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(RedirectUri + "?code=");
        result.Value.ClearAppMfaTicket.Should().BeFalse();
        result.Value.ClearStepUpTicket.Should().BeFalse();
    }

    // ── reason in the handle shape (OI-145) ───────────────────────────────────

    [Fact]
    public async Task Handle_HandleShape_CarriesTheReasonBesideTheHandle_AndNeverStoresIt()
    {
        _settings.AuthorizationRequestHandlesEnabled = true;
        AuthorizationRequest? stored = null;
        _requests.Setup(r => r.CreateAsync(It.IsAny<AuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuthorizationRequest r, CancellationToken _) => stored = r)
            .Returns(Task.CompletedTask);
        Application();
        Session(PasswordOnly, hasFactor: false);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.RedirectUrl.Should().MatchRegex(
            @"^https://accounts\.example\.com/login\?client_id=CRM&auth_request=[^&]+&reason=mfa_enroll&method=password$");
        result.Value.RedirectUrl.Split('?')[1].Should().NotContain("://", "a firewall reads an address in a parameter as an inclusion (OI-145)");
        stored.Should().NotBeNull("the request is stored under the handle; the reason is not part of it");
    }

    // ── the method word beside the reason (S23 PR B, D-S23-3 (c)) ────────────

    private static System.Collections.Specialized.NameValueCollection QueryOf(string url) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);

    [Theory]
    [InlineData(true, false, "password")]
    [InlineData(false, false, "code")]
    [InlineData(true, true, "password")]
    [InlineData(false, true, "code")]
    public async Task Handle_TheMethodWord_FollowsWhetherTheAccountHasAPassword_InBothShapes(
        bool hasPassword, bool handles, string method)
    {
        _settings.AuthorizationRequestHandlesEnabled = handles;
        AuthorizationRequest? stored = null;
        _requests.Setup(r => r.CreateAsync(It.IsAny<AuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuthorizationRequest r, CancellationToken _) => stored = r)
            .Returns(Task.CompletedTask);
        Application();
        // What each kind of account's sign-in records: a password, or the emailed code as
        // the first proof of an account that has none (D-215-1).
        Session(
            hasPassword ? PasswordOnly : AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false),
            hasFactor: false,
            hasPassword: hasPassword);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var url = result.Value.RedirectUrl;
        url.Should().StartWith(handles
            ? "https://accounts.example.com/login?client_id=CRM&auth_request="
            : "https://accounts.example.com/login?returnTo=");
        url.Should().EndWith($"&reason=mfa_enroll&method={method}", "the method word follows the reason, nothing between them");
        QueryOf(url).GetValues("method").Should().Equal(method);
        if (handles)
        {
            // The stored request has no field for either word, and a field added later
            // must not start holding them: the resume decides again from the session.
            stored.Should().NotBeNull();
            typeof(AuthorizationRequest).GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => (string?)p.GetValue(stored))
                .Should().NotContain(value => value != null &&
                    (value.Contains("mfa_", StringComparison.Ordinal) || value.Contains("method", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ASignInRedirectWithoutAReason_CarriesNoMethodWord(bool satisfiedSessionWithPromptLogin)
    {
        Application();
        AuthorizeCommand command;
        if (satisfiedSessionWithPromptLogin)
        {
            // The freshness check's own redirect: nothing about the application's requirement.
            Session(PasswordAndTotp);
            command = Command(prompt: "login");
        }
        else
        {
            // No SSO session at all: the ordinary sign-in redirect.
            command = Command();
        }

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login?");
        var query = QueryOf(result.Value.RedirectUrl);
        query.AllKeys.Should().NotContain("reason").And.NotContain("method");
    }
}
