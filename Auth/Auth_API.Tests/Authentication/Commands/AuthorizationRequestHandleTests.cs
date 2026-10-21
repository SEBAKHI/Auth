using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-145: the accounts pages carry <c>client_id</c> and a 64-hex handle to a
/// stored authorize request, never the authorize URL itself, which a web
/// application firewall refuses as a remote file inclusion. The handler runs with
/// the shipped settings (handles on, D-145-1 (b)) and an in-memory store, so a
/// request stored by one call is the one the next call resumes.
/// </summary>
public class AuthorizationRequestHandleTests
{
    private const string ClientId = "CRM";
    private const string OtherClientId = "OTHER";
    private const string RedirectUri = "https://app.example.com/callback";
    private const string Accounts = "https://accounts.example.com";
    private const string OriginalUrl = "https://auth.example.com/api/v1/auth/authorize?response_type=code&client_id=CRM";
    private static readonly string ValidChallenge = new('a', 43);
    private static readonly Regex HexHandle = new("^[0-9a-f]{64}$");

    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IIdpSessionRepository> _sessions = new();
    private readonly Mock<IAuthorizationCodeRepository> _codes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IJwtTokenService> _tokens = new();
    private readonly FakeCredentialHasher _keys = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly InMemoryAuthorizationRequests _store = new();
    private readonly List<AuthorizationCode> _issued = [];
    private readonly List<string> _generated = [];
    private readonly CapturingLogger<AuthorizeCommandHandler> _logger = new();
    private readonly IStepUpTicketService _stepUp;

    // The system time unless a test walks a timeline (OI-150).
    private readonly AdjustableTimeProvider _clock = new();

    // The shipped defaults: nothing here turns the switch on.
    private readonly IdentityProviderSettings _settings = new() { AccountsBaseUrl = Accounts };

    private readonly AuthorizeCommandHandler _handler;

    public AuthorizationRequestHandleTests()
    {
        _access
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _keys.MapAll(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"hash:{value}")));
        // The real generator's shape since OI-142: 32 random bytes as lowercase hex.
        _tokens
            .Setup(s => s.GenerateRefreshToken())
            .Returns(() =>
            {
                var value = RandomNumberGenerator.GetHexString(64, lowercase: true);
                _generated.Add(value);
                return value;
            });
        _codes
            .Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationCode, CancellationToken>((code, _) => _issued.Add(code))
            .ReturnsAsync((AuthorizationCode code, CancellationToken _) => code);

        _stepUp = new Auth.Infrastructure.Authentication.StepUpTicketService(_keys);

        _handler = new AuthorizeCommandHandler(
            _applications.Object,
            _access.Object,
            TestIdpSessions.Reader(_sessions, _keys, _settings, _clock),
            _codes.Object,
            _store,
            _users.Object,
            _tokens.Object,
            _keys,
            _stepUp,
            _organizations.Object,
            new Auth.Application.Common.OrganizationCreatorRoleCheck(_roles.Object, _permissions.Object),
            TestHelpers.CreateOptions(_settings),
            _clock,
            _logger);
    }

    #region Arrangement

    private static AuthorizeCommand Command(
        string? prompt = null,
        string? maxAge = null,
        string? scope = null,
        string? createOrganization = null,
        string? state = "xyz",
        string? idpSessionToken = null,
        string? stepUpTicket = null) => new(
            "code",
            ClientId,
            RedirectUri,
            ValidChallenge,
            "S256",
            state,
            idpSessionToken,
            OriginalUrl,
            "127.0.0.1",
            prompt,
            maxAge,
            stepUpTicket,
            scope,
            createOrganization);

    private ResumeAuthorizationCommand Resume(
        string? handle,
        string? clientId = ClientId,
        bool skipOrganization = false,
        string? idpSessionToken = null,
        string? stepUpTicket = null) =>
        new(clientId, handle, skipOrganization, idpSessionToken, stepUpTicket, "127.0.0.1");

    private Auth.Domain.Entities.Application SetupApplication(
        string code = ClientId, bool isActive = true, string? allowedScopes = "profile email")
    {
        var application = TestHelpers.CreateApplication(code: code, isActive: isActive);
        application.LoadRedirectUris([RedirectUri]);
        application.LoadAllowedScopes(allowedScopes);

        _applications
            .Setup(r => r.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        return application;
    }

    /// <summary>A valid session <paramref name="age"/> old; returns its cookie value.</summary>
    private (string Token, Guid UserId) SetupSession(TimeSpan age = default)
    {
        var userId = Guid.NewGuid();
        var createdAt = _clock.UtcNow - age;
        var session = new IdpSession(
            Guid.NewGuid(), userId, "idp-hash", createdAt, createdAt.AddDays(7), null, null, null);

        _keys.Map("idp-token", "idp-hash");
        _sessions
            .Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("idp-hash"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _users
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        return ("idp-token", userId);
    }

    private void SetupCreatorRole(Auth.Domain.Entities.Application application)
    {
        var roleId = Guid.NewGuid();
        application.LoadOrganizationCreation(true, roleId);
        _roles
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: application.Id, isActive: true));
        _permissions
            .Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(code: "crm:perm0")]);
    }

    private void SetupOrganization(Guid userId, Guid? organizationId) =>
        _organizations
            .Setup(r => r.FindOrganizationSetUpForApplicationAsync(
                userId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(organizationId);

    private string Hash(string value) => _keys.Of(value);

    /// <summary>A stored request for <see cref="ClientId"/> under <paramref name="handle"/>.</summary>
    private AuthorizationRequest StoreDirectly(
        string handle, Guid applicationId, DateTime createdAt, TimeSpan lifetime)
    {
        var row = AuthorizationRequest.Create(
            Hash(handle), applicationId, ClientId, "code", RedirectUri, ValidChallenge, "S256",
            "xyz", null, null, "openid", createOrganization: false, lifetime, createdAt);
        _store.Rows.Add(row);
        return row;
    }

    private static Dictionary<string, string> QueryOf(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

    private static string HandleIn(string url) => QueryOf(url)["auth_request"];

    /// <summary>
    /// Everything a relying party or a browser can observe about an authorize
    /// answer, with the random code replaced by what it was bound to.
    /// </summary>
    private static object Outcome(AuthorizeResult result, AuthorizationCode? code)
    {
        var uri = new Uri(result.RedirectUrl);
        var query = QueryOf(result.RedirectUrl);

        return new
        {
            Target = uri.GetLeftPart(UriPartial.Path),
            Error = query.GetValueOrDefault("error"),
            State = query.GetValueOrDefault("state"),
            CodeIssued = query.ContainsKey("code"),
            Grant = code is null
                ? null
                : new { code.ApplicationId, code.UserId, code.RedirectUri, code.CodeChallenge, code.Scope },
            result.IsLoginRedirect,
            StepUpDemanded = result.StepUpTicketToSet is not null,
            result.ClearStepUpTicket
        };
    }

    private async Task<(AuthorizeResult Result, AuthorizationCode? Code)> RunAsync(
        Func<Task<ErrorOr.ErrorOr<AuthorizeResult>>> call)
    {
        var before = _issued.Count;
        var result = await call();
        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        return (result.Value, _issued.Count > before ? _issued[^1] : null);
    }

    #endregion

    #region T1: the accounts redirect carries client_id and a handle, nothing else

    public static TheoryData<string> AccountsPages => new() { "login", "register", "create-organization" };

    [Fact]
    public void AccountsPages_AreNotEmpty() => AccountsPages.Count.Should().BeGreaterThan(0);

    /// <summary>Each accounts page an authorize request can be sent to, reached the way it is reached.</summary>
    private async Task<AuthorizeResult> ReachAsync(string page)
    {
        var application = SetupApplication();
        switch (page)
        {
            case "login":
                return (await _handler.Handle(Command(), CancellationToken.None)).Value;
            case "register":
                return (await _handler.Handle(Command(prompt: "create"), CancellationToken.None)).Value;
            case "create-organization":
                SetupCreatorRole(application);
                var (token, userId) = SetupSession();
                SetupOrganization(userId, null);
                return (await _handler.Handle(
                    Command(createOrganization: "true", idpSessionToken: token), CancellationToken.None)).Value;
            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, null);
        }
    }

    [Theory]
    [MemberData(nameof(AccountsPages))]
    public async Task Authorize_ToAnAccountsPage_CarriesOnlyTheClientIdAndAHandle(string page)
    {
        var result = await ReachAsync(page);

        result.RedirectUrl.Should().StartWith($"{Accounts}/{page}?");
        var query = QueryOf(result.RedirectUrl);
        query.Keys.Should().BeEquivalentTo(["client_id", "auth_request"], "no other parameter rides along");
        query["client_id"].Should().Be(ClientId);
        query["auth_request"].Should().MatchRegex(HexHandle);
        query.Values.Should().NotContain(value => value.Contains("://"),
            "a value holding an off-site address is what the firewall refused");
        new Uri(result.RedirectUrl).Query.Should().NotContainEquivalentOf("%3A%2F%2F");
    }

    [Theory]
    [MemberData(nameof(AccountsPages))]
    public async Task Authorize_ToAnAccountsPage_StoresOneRowUnderTheHashNeverTheHandle(string page)
    {
        var result = await ReachAsync(page);
        var handle = HandleIn(result.RedirectUrl);

        var row = _store.Rows.Should().ContainSingle().Subject;
        row.HandleHash.Should().Be(Hash(handle));
        typeof(AuthorizationRequest).GetProperties()
            .Select(property => property.GetValue(row)?.ToString())
            .Should().NotContain(handle, "only the hash of the handle is stored");
    }

    [Fact]
    public async Task Authorize_StoresTheValidatedValues_NormalisedAndBounded()
    {
        var application = SetupApplication();
        var before = DateTime.UtcNow;

        await _handler.Handle(
            Command(prompt: "  LOGIN consent  ", maxAge: "120", scope: "email  openid offline_access email", state: "s 1/2"),
            CancellationToken.None);

        var row = _store.Rows.Should().ContainSingle().Subject;
        row.ApplicationId.Should().Be(application.Id);
        row.ClientId.Should().Be(ClientId);
        row.ResponseType.Should().Be("code");
        row.RedirectUri.Should().Be(RedirectUri);
        row.CodeChallenge.Should().Be(ValidChallenge);
        row.CodeChallengeMethod.Should().Be("S256");
        row.State.Should().Be("s 1/2", "state is stored verbatim");
        row.Prompt.Should().Be("login", "the recognised values in canonical order; consent is ignored at authorize too");
        row.MaxAge.Should().Be(120);
        row.Scope.Should().Be("openid email", "the canonical requested set");
        row.CreateOrganization.Should().BeFalse();
        row.CreatedAt.Should().BeOnOrAfter(before);
        row.ExpiresAt.Should().BeCloseTo(row.CreatedAt.AddMinutes(60), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Authorize_MalformedMaxAge_IsStoredAsAbsent_AsAuthorizeIgnoresIt()
    {
        SetupApplication();

        await _handler.Handle(Command(maxAge: "soon"), CancellationToken.None);

        _store.Rows.Should().ContainSingle().Which.MaxAge.Should().BeNull();
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(0, 5)]
    [InlineData(100_000, 1440)]
    public async Task Authorize_ExpiryIsTheConfiguredLifetime_HeldToItsRange(int configured, int expected)
    {
        _settings.AuthorizationRequestLifetimeMinutes = configured;
        SetupApplication();

        await _handler.Handle(Command(), CancellationToken.None);

        var row = _store.Rows.Should().ContainSingle().Subject;
        (row.ExpiresAt - row.CreatedAt).Should().Be(TimeSpan.FromMinutes(expected));
    }

    [Fact]
    public async Task Authorize_PromptNone_NeverReachesAnAccountsPage_SoStoresNothing()
    {
        SetupApplication();

        var result = await _handler.Handle(Command(prompt: "none"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{RedirectUri}?error=login_required&state=xyz");
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Authorize_WithACode_StoresNothing()
    {
        SetupApplication();
        var (token, _) = SetupSession();

        var (result, code) = await RunAsync(() => _handler.Handle(Command(idpSessionToken: token), CancellationToken.None));

        result.RedirectUrl.Should().StartWith($"{RedirectUri}?code=");
        code.Should().NotBeNull();
        _store.Rows.Should().BeEmpty("only the two accounts redirects store a request");
    }

    [Fact]
    public async Task Authorize_StoreFails_Throws_AndNeverFallsBackToTheFullUrl()
    {
        SetupApplication();
        _store.FailInsertWith = new InvalidOperationException("database unavailable");

        var act = () => _handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a silent fallback to returnTo would hand the browser to the firewall again");
    }

    [Fact]
    public async Task Authorize_StepUpDemand_StillSetsItsCookie_WithTheHandleRedirect()
    {
        SetupApplication();
        var (token, _) = SetupSession();

        var result = await _handler.Handle(Command(prompt: "login", idpSessionToken: token), CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{Accounts}/login?client_id={ClientId}&auth_request=");
        result.Value.StepUpTicketToSet.Should().NotBeNull();
        result.Value.IsLoginRedirect.Should().BeTrue();
    }

    [Fact]
    public async Task NoLogLine_CarriesTheHandle()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        await _handler.Handle(Resume(handle), CancellationToken.None);
        await _handler.Handle(Resume(handle, clientId: OtherClientId), CancellationToken.None);

        _logger.Entries.Should().NotBeEmpty();
        _logger.Entries.Should().NotContain(entry =>
            entry.Message.Contains(handle) || entry.Values.Any(value => value.Contains(handle)));
    }

    #endregion

    #region T2: resuming a stored request ends exactly where a direct authorize ends

    /// <summary>
    /// The authorize parameters of <see cref="AuthController.Authorize"/>, each with
    /// a distinctive valid value and the value its stored field must hold.
    /// </summary>
    private static readonly Dictionary<string, (string Sent, Func<AuthorizationRequest, object?> Stored, object? Expected)> StoredFields = new()
    {
        ["response_type"] = ("code", row => row.ResponseType, "code"),
        ["client_id"] = (ClientId, row => row.ClientId, ClientId),
        ["redirect_uri"] = (RedirectUri, row => row.RedirectUri, RedirectUri),
        ["code_challenge"] = (new string('c', 50), row => row.CodeChallenge, new string('c', 50)),
        ["code_challenge_method"] = ("S256", row => row.CodeChallengeMethod, "S256"),
        ["state"] = ("st/9 x", row => row.State, "st/9 x"),
        ["prompt"] = ("create login", row => row.Prompt, "login create"),
        ["max_age"] = ("321", row => row.MaxAge, 321),
        ["scope"] = ("profile openid", row => row.Scope, "openid profile"),
        ["create_organization"] = ("true", row => row.CreateOrganization, true),
    };

    [Fact]
    public async Task EveryAuthorizeQueryParameter_IsStored()
    {
        // Reflection, so a parameter added to the endpoint without a column here
        // fails this test instead of vanishing silently on every resume.
        var parameters = typeof(AuthController).GetMethod(nameof(AuthController.Authorize))!
            .GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<FromQueryAttribute>()?.Name)
            .OfType<string>()
            .ToList();

        parameters.Should().HaveCountGreaterThan(0);
        parameters.Should().BeEquivalentTo(StoredFields.Keys,
            "every authorize parameter needs a stored field, or a resume would drop it");

        var application = SetupApplication();
        SetupCreatorRole(application);
        var sent = StoredFields.ToDictionary(field => field.Key, field => field.Value.Sent);

        var result = await _handler.Handle(
            new AuthorizeCommand(
                sent["response_type"], sent["client_id"], sent["redirect_uri"], sent["code_challenge"],
                sent["code_challenge_method"], sent["state"], IdpSessionToken: null, OriginalUrl, "127.0.0.1",
                sent["prompt"], sent["max_age"], StepUpTicket: null, sent["scope"], sent["create_organization"]),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith($"{Accounts}/register?");
        var row = _store.Rows.Should().ContainSingle().Subject;
        foreach (var (name, field) in StoredFields)
        {
            field.Stored(row).Should().Be(field.Expected, $"{name} must be stored as authorize read it");
        }

        // And read back: the command a resume runs carries every parameter again,
        // in a form authorize reads to the same decision.
        var resumed = AuthorizeCommandHandler.FromStored(row, Resume(HandleIn(result.Value.RedirectUrl)));
        ResumedFields.Keys.Should().BeEquivalentTo(parameters, "every authorize parameter must be read back on a resume");
        foreach (var (name, field) in ResumedFields)
        {
            field.Read(resumed).Should().Be(field.Expected, $"{name} must reach the resumed request");
        }
    }

    /// <summary>
    /// Each authorize parameter as the resumed command carries it, for the sent
    /// values of <see cref="StoredFields"/>: canonical where authorize reads loosely.
    /// </summary>
    private static readonly Dictionary<string, (Func<AuthorizeCommand, string?> Read, string? Expected)> ResumedFields = new()
    {
        ["response_type"] = (command => command.ResponseType, "code"),
        ["client_id"] = (command => command.ClientId, ClientId),
        ["redirect_uri"] = (command => command.RedirectUri, RedirectUri),
        ["code_challenge"] = (command => command.CodeChallenge, new string('c', 50)),
        ["code_challenge_method"] = (command => command.CodeChallengeMethod, "S256"),
        ["state"] = (command => command.State, "st/9 x"),
        ["prompt"] = (command => command.Prompt, "login create"),
        ["max_age"] = (command => command.MaxAge, "321"),
        ["scope"] = (command => command.Scope, "openid profile"),
        ["create_organization"] = (command => command.CreateOrganization, "true"),
    };

    public static TheoryData<string> EquivalenceCases => new()
    {
        "code with state and grant",
        "scope dropped",
        "prompt=login, not yet proved",
        "prompt=login, proved",
        "max_age exceeded",
        "max_age met",
        "organization required",
        "organization present",
        "prompt=create without a session",
        "session gone",
    };

    [Fact]
    public void EquivalenceCases_AreNotEmpty() => EquivalenceCases.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(EquivalenceCases))]
    public async Task Resume_EndsWhereADirectAuthorizeEnds(string name)
    {
        var application = SetupApplication();
        var command = name switch
        {
            "code with state and grant" => Command(scope: "openid profile email", state: "s-1 /x"),
            "scope dropped" => Command(scope: "openid phone email"),
            "prompt=login, not yet proved" or "prompt=login, proved" => Command(prompt: "login"),
            "max_age exceeded" or "max_age met" => Command(maxAge: "60"),
            "organization required" or "organization present" => Command(createOrganization: "true"),
            "prompt=create without a session" => Command(prompt: "create"),
            "session gone" => Command(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
        if (command.CreateOrganization is not null)
        {
            SetupCreatorRole(application);
        }

        // The stored request: what the first, sessionless trip through authorize kept.
        var first = await _handler.Handle(command, CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);

        // The browser as it comes back from the accounts page.
        string? token = null;
        string? ticket = null;
        switch (name)
        {
            case "prompt=login, not yet proved":
            case "code with state and grant":
            case "scope dropped":
            case "max_age met":
                token = SetupSession().Token;
                break;
            case "prompt=login, proved":
                ticket = _stepUp.Issue(ClientId, ValidChallenge, _clock.UtcNow.AddSeconds(-30));
                token = SetupSession(TimeSpan.FromSeconds(5)).Token;
                break;
            case "max_age exceeded":
                token = SetupSession(TimeSpan.FromMinutes(10)).Token;
                break;
            case "organization required":
            case "organization present":
                (token, var userId) = SetupSession();
                SetupOrganization(userId, name == "organization present" ? Guid.NewGuid() : null);
                break;
        }

        var direct = await RunAsync(() => _handler.Handle(
            command with { IdpSessionToken = token, StepUpTicket = ticket }, CancellationToken.None));
        var resumed = await RunAsync(() => _handler.Handle(
            Resume(handle, idpSessionToken: token, stepUpTicket: ticket), CancellationToken.None));

        Outcome(resumed.Result, resumed.Code).Should().BeEquivalentTo(Outcome(direct.Result, direct.Code));
    }

    [Fact]
    public async Task Resume_IssuesTheCodeForTheStoredRequest_AndKeepsTheRowForAReplay()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(scope: "openid email", state: "rp-state"), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        var (token, userId) = SetupSession();

        var (result, code) = await RunAsync(() =>
            _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None));

        result.RedirectUrl.Should().StartWith($"{RedirectUri}?code=").And.EndWith("&state=rp-state");
        code!.UserId.Should().Be(userId);
        code.Scope.Should().Be("openid email");
        code.CodeChallenge.Should().Be(ValidChallenge);
        _store.Rows.Should().ContainSingle("a used request is not deleted: the back button replays authorize");

        var replay = await RunAsync(() => _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None));
        replay.Code.Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_RunsEveryCheckAgain_AClientSwitchedOffSinceFailsAsAFreshRequest()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        SetupApplication(isActive: false);

        var result = await _handler.Handle(Resume(handle), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(Auth.Domain.Errors.AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task Resume_RunsEveryCheckAgain_ARedirectUriUnregisteredSinceFails()
    {
        var application = SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        application.LoadRedirectUris(["https://app.example.com/other"]);

        var result = await _handler.Handle(Resume(handle), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(Auth.Domain.Errors.AuthErrors.InvalidRedirectUri);
    }

    #endregion

    #region T3: a resume that needs an accounts page again keeps its handle

    [Fact]
    public async Task Resume_ThatNeedsSignInAgain_ReusesTheHandle_AndStoresNoSecondRow()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);

        var again = await _handler.Handle(Resume(handle), CancellationToken.None);

        again.Value.RedirectUrl.Should().Be($"{Accounts}/login?client_id={ClientId}&auth_request={handle}");
        again.Value.IsLoginRedirect.Should().BeTrue();
        _store.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Resume_ThatNeedsAnOrganization_ReusesTheHandle()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        var first = await _handler.Handle(Command(createOrganization: "true"), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        var (token, userId) = SetupSession();
        SetupOrganization(userId, null);

        var result = await _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(
            $"{Accounts}/create-organization?client_id={ClientId}&auth_request={handle}");
        _store.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Resume_SkipOrganization_IssuesTheCodeWithoutAnOrganization_ForGood()
    {
        var application = SetupApplication();
        SetupCreatorRole(application);
        var (token, userId) = SetupSession();
        SetupOrganization(userId, null);
        var first = await _handler.Handle(
            Command(createOrganization: "true", idpSessionToken: token), CancellationToken.None);
        first.Value.RedirectUrl.Should().StartWith($"{Accounts}/create-organization?");
        var handle = HandleIn(first.Value.RedirectUrl);

        var (result, code) = await RunAsync(() => _handler.Handle(
            Resume(handle, skipOrganization: true, idpSessionToken: token), CancellationToken.None));

        result.RedirectUrl.Should().StartWith($"{RedirectUri}?code=");
        code.Should().NotBeNull();
        var row = _store.Rows.Should().ContainSingle().Subject;
        row.CreateOrganization.Should().BeFalse();
        _store.Cleared.Should().Equal(row.Id);

        // Persisted: a later trip through the same request does not ask again.
        var later = await RunAsync(() => _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None));
        later.Code.Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_SkipOrganization_OnARequestThatAsksForNone_WritesNothing()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);

        await _handler.Handle(Resume(handle, skipOrganization: true), CancellationToken.None);

        _store.Cleared.Should().BeEmpty();
    }

    #endregion

    #region T4: anything else ends on the expired page, naming only an active application

    public static TheoryData<string?> MalformedHandles => new()
    {
        null,
        "",
        "abc",
        new string('A', 64),
        new string('a', 63),
        new string('a', 65),
        new string('g', 64),
        new string('a', 64) + "\n",
        " " + new string('a', 63),
        "https://evil.example.com/" + new string('a', 39),
    };

    [Fact]
    public void MalformedHandles_AreNotEmpty() => MalformedHandles.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(MalformedHandles))]
    public async Task Resume_MalformedHandle_GoesToTheExpiredPage_WithoutReadingTheStore(string? handle)
    {
        SetupApplication();

        var result = await _handler.Handle(Resume(handle), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Accounts}/sign-in-expired?client_id={ClientId}");
        _store.Reads.Should().Be(0);
        _issued.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_UnknownHandle_GoesToTheExpiredPage()
    {
        SetupApplication();

        var result = await _handler.Handle(
            Resume(RandomNumberGenerator.GetHexString(64, lowercase: true)), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Accounts}/sign-in-expired?client_id={ClientId}");
        _store.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resume_ExpiredHandle_GoesToTheExpiredPage(bool storeReturnsExpiredRows)
    {
        // Both halves of the rule: the query filters on ExpiresAt, and the handler
        // checks again, so a store that returned an expired row resumes nothing.
        var application = SetupApplication();
        var (token, _) = SetupSession();
        var handle = RandomNumberGenerator.GetHexString(64, lowercase: true);
        StoreDirectly(handle, application.Id, DateTime.UtcNow.AddHours(-2), TimeSpan.FromMinutes(60));
        _store.ReturnExpiredRows = storeReturnsExpiredRows;

        var result = await _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Accounts}/sign-in-expired?client_id={ClientId}");
        _issued.Should().BeEmpty();
    }

    public static TheoryData<string?, string?> MismatchedClients => new()
    {
        // The page names an active application: the expired page shows it.
        { OtherClientId, OtherClientId },
        // Not the same code to an ordinal comparison, and not an application here.
        { "crm", null },
        // Not an application at all, or not an active one: nothing is echoed.
        { "NOPE", null },
        { "INACTIVE", null },
        { null, null },
    };

    [Fact]
    public void MismatchedClients_AreNotEmpty() => MismatchedClients.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(MismatchedClients))]
    public async Task Resume_ClientIdThatIsNotTheStoredOnes_GoesToTheExpiredPage(
        string? presented, string? shown)
    {
        SetupApplication();
        SetupApplication(code: OtherClientId);
        SetupApplication(code: "INACTIVE", isActive: false);
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        var (token, _) = SetupSession();

        var result = await _handler.Handle(
            Resume(handle, clientId: presented, idpSessionToken: token), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(shown is null
            ? $"{Accounts}/sign-in-expired"
            : $"{Accounts}/sign-in-expired?client_id={shown}");
        result.Value.RedirectUrl.Should().NotContain(handle);
        _issued.Should().BeEmpty("another client's request must never issue a code");
    }

    #endregion

    #region T5: with the switch off, the legacy shape returns and live handles still resume

    [Fact]
    public async Task SwitchOff_AuthorizeSendsTheLegacyReturnTo_AndStoresNothing()
    {
        _settings.AuthorizationRequestHandlesEnabled = false;
        SetupApplication();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Accounts}/login?returnTo={Uri.EscapeDataString(OriginalUrl)}");
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchOff_ALiveHandleStillResumes_AndStillKeepsItsHandle()
    {
        SetupApplication();
        var first = await _handler.Handle(Command(), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        _settings.AuthorizationRequestHandlesEnabled = false;

        var again = await _handler.Handle(Resume(handle), CancellationToken.None);
        again.Value.RedirectUrl.Should().Be($"{Accounts}/login?client_id={ClientId}&auth_request={handle}");

        var (token, _) = SetupSession();
        var (result, code) = await RunAsync(() =>
            _handler.Handle(Resume(handle, idpSessionToken: token), CancellationToken.None));
        result.RedirectUrl.Should().StartWith($"{RedirectUri}?code=");
        code.Should().NotBeNull();
        _store.Rows.Should().ContainSingle();
    }

    [Fact]
    public void TheShippedDefault_IsOn_InTheSettingsClassAndTheRegistry()
    {
        // D-145-1 (b). The file value is pinned with the other production defaults.
        new IdentityProviderSettings().AuthorizationRequestHandlesEnabled.Should().BeTrue();
        Auth.Application.SystemSettings.SystemSettingsRegistry.Sections
            .Single(section => section.ConfigRoot == "IdentityProvider")
            .Fields.Single(field => field.Path == "AuthorizationRequestHandlesEnabled")
            .DefaultValue.Should().Be(true);
    }

    #endregion

    #region OI-150: the stored request replays its challenge, so its ticket answers its resume and nothing else

    [Fact]
    public async Task StepUp_TheStoredRequestResumesWithItsChallenge_ThroughTheOrganizationStepToItsCode()
    {
        // T7 through the stored row (handles on): every resume replays the
        // challenge the request was stored with, so the ticket raised on the
        // first trip answers each of them, and is spent with the code.
        var application = SetupApplication();
        SetupCreatorRole(application);
        _clock.Advance(TimeSpan.Zero);

        var first = await _handler.Handle(
            Command(prompt: "create login", createOrganization: "true"), CancellationToken.None);
        first.Value.RedirectUrl.Should().StartWith($"{Accounts}/register?client_id={ClientId}&auth_request=");
        var handle = HandleIn(first.Value.RedirectUrl);
        var cookie = first.Value.StepUpTicketToSet;
        cookie.Should().NotBeNull();

        // Signed up and signed in: the resume is proved and goes to the
        // organization page, the ticket unspent.
        _clock.Advance(TimeSpan.FromMinutes(8));
        var (token, userId) = SetupSession();
        SetupOrganization(userId, null);
        var toOrganization = await RunAsync(() => _handler.Handle(
            Resume(handle, idpSessionToken: token, stepUpTicket: cookie), CancellationToken.None));
        toOrganization.Result.RedirectUrl.Should().Be(
            $"{Accounts}/create-organization?client_id={ClientId}&auth_request={handle}");
        toOrganization.Result.ClearStepUpTicket.Should().BeFalse();
        toOrganization.Code.Should().BeNull();

        // The organization is set up inside the finish window; the page resumes.
        _clock.Advance(TimeSpan.FromMinutes(2));
        SetupOrganization(userId, Guid.NewGuid());
        var (result, code) = await RunAsync(() => _handler.Handle(
            Resume(handle, idpSessionToken: token, stepUpTicket: cookie), CancellationToken.None));

        result.RedirectUrl.Should().StartWith($"{RedirectUri}?code=");
        code!.CodeChallenge.Should().Be(ValidChallenge);
        result.ClearStepUpTicket.Should().BeTrue();
    }

    [Fact]
    public async Task StepUp_ATicketProvedForAStoredRequest_DoesNotAnswerANewRequest()
    {
        // T6 through the stored row: the next person's NEW request carries its
        // own challenge, and the first person's ticket does not answer it.
        var application = SetupApplication();
        SetupCreatorRole(application);
        _clock.Advance(TimeSpan.Zero);
        var first = await _handler.Handle(
            Command(prompt: "create login", createOrganization: "true"), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        var cookie = first.Value.StepUpTicketToSet;

        _clock.Advance(TimeSpan.FromMinutes(1));
        var (token, userId) = SetupSession();
        SetupOrganization(userId, null);
        (await _handler.Handle(Resume(handle, idpSessionToken: token, stepUpTicket: cookie), CancellationToken.None))
            .Value.RedirectUrl.Should().StartWith($"{Accounts}/create-organization?");

        _clock.Advance(TimeSpan.FromMinutes(2));
        var (result, code) = await RunAsync(() => _handler.Handle(
            Command(prompt: "login", idpSessionToken: token, stepUpTicket: cookie) with { CodeChallenge = new string('z', 43) },
            CancellationToken.None));

        result.IsLoginRedirect.Should().BeTrue();
        result.RedirectUrl.Should().StartWith($"{Accounts}/login?client_id={ClientId}&auth_request=");
        result.StepUpTicketToSet.Should().NotBeNull().And.NotBe(cookie);
        code.Should().BeNull();
    }

    [Fact]
    public async Task StepUp_TheStoredRequestSixMinutesAfterTheSignIn_AsksToAuthenticateAgain()
    {
        // T13 through the stored row: the page left open, or brought back with
        // the Back button, resumes the same handle after the finish window, and
        // the person authenticates again (registration, since the request
        // carries create, with its sign-in link).
        var application = SetupApplication();
        SetupCreatorRole(application);
        _clock.Advance(TimeSpan.Zero);
        var first = await _handler.Handle(
            Command(prompt: "create login", createOrganization: "true"), CancellationToken.None);
        var handle = HandleIn(first.Value.RedirectUrl);
        var cookie = first.Value.StepUpTicketToSet;

        _clock.Advance(TimeSpan.FromMinutes(1));
        var (token, userId) = SetupSession();
        SetupOrganization(userId, null);
        (await _handler.Handle(Resume(handle, idpSessionToken: token, stepUpTicket: cookie), CancellationToken.None))
            .Value.RedirectUrl.Should().StartWith($"{Accounts}/create-organization?");

        _clock.Advance(TimeSpan.FromMinutes(6));
        SetupOrganization(userId, Guid.NewGuid());
        var (result, code) = await RunAsync(() => _handler.Handle(
            Resume(handle, idpSessionToken: token, stepUpTicket: cookie), CancellationToken.None));

        result.IsLoginRedirect.Should().BeTrue();
        result.RedirectUrl.Should().Be($"{Accounts}/register?client_id={ClientId}&auth_request={handle}");
        result.StepUpTicketToSet.Should().NotBeNull();
        code.Should().BeNull();
    }

    #endregion

    /// <summary>
    /// The table as the handler sees it: rows by handle hash, live until they expire.
    /// </summary>
    private sealed class InMemoryAuthorizationRequests : IAuthorizationRequestRepository
    {
        public List<AuthorizationRequest> Rows { get; } = [];
        public List<Guid> Cleared { get; } = [];
        public int Reads { get; private set; }
        public Exception? FailInsertWith { get; set; }

        /// <summary>A store whose query forgot the expiry filter, to prove the handler's own check.</summary>
        public bool ReturnExpiredRows { get; set; }

        public Task CreateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            if (FailInsertWith is not null)
            {
                throw FailInsertWith;
            }

            Rows.Should().NotContain(row => row.HandleHash == request.HandleHash, "the handle hash is unique");
            Rows.Add(request);
            return Task.CompletedTask;
        }

        public Task<AuthorizationRequest?> GetLiveByHandleHashAsync(
            CredentialHashCandidates candidates, DateTime nowUtc, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Rows.SingleOrDefault(row =>
                candidates.Contains(row.HandleHash) && (ReturnExpiredRows || row.ExpiresAt > nowUtc)));
        }

        public Task ClearCreateOrganizationAsync(Guid id, CancellationToken cancellationToken)
        {
            Cleared.Add(id);
            return Task.CompletedTask;
        }

        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
            => Task.FromResult(Rows.RemoveAll(row => row.ExpiresAt < olderThanUtc));
    }
}
