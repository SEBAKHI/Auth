using System.Text.Json;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.OrganizationSetup;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Organizations;

/// <summary>
/// The organization-creation step (OI-63): who it acts for, the order of its
/// checks, the code it generates, and what it publishes. The transaction itself
/// is guarded in <c>OrganizationProvisioningSqlTests</c>.
/// </summary>
public class OrganizationSetupHandlerTests
{
    private const string ClientId = "SHOP";
    private const string IdpToken = "idp-token";

    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly OrganizationSettings _settings = new();
    private readonly List<string> _calls = [];

    private readonly Auth.Domain.Entities.Application _application;
    private readonly Guid _creatorRoleId = Guid.NewGuid();
    private readonly Guid _ownerRoleId = Guid.NewGuid();
    private User _user = TestHelpers.CreateUser();

    public OrganizationSetupHandlerTests()
    {
        _application = TestHelpers.CreateApplication(code: ClientId, accessMode: ApplicationAccessMode.Everyone);
        _application.LoadOrganizationCreation(true, _creatorRoleId);
        _applications.Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);

        _roles.Setup(r => r.GetByIdAsync(_creatorRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: _creatorRoleId, applicationId: _application.Id));
        _permissions.Setup(r => r.GetRolePermissionsAsync(_creatorRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(code: "shop:customers:manage")]);
        _roles.Setup(r => r.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: _ownerRoleId, code: OrganizationRoleCodes.Owner));

        _keys.Setup(k => k.ComputeTokenHash(IdpToken)).Returns("idp-hash");
        SignIn(_user);

        _publisher
            .Setup(p => p.Publish(It.IsAny<OrganizationProvisionedForApplicationEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("publish"))
            .Returns(Task.CompletedTask);
    }

    private void SignIn(User user)
    {
        _user = user;
        var session = new IdpSession(
            Guid.NewGuid(), user.Id, "idp-hash", DateTime.UtcNow, DateTime.UtcNow.AddDays(7), null, null, null);
        _idpSessions.Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private OrganizationSetupSession Session() => new(
        _idpSessions.Object, _keys.Object, _users.Object, _applications.Object,
        new OrganizationCreatorRoleCheck(_roles.Object, _permissions.Object),
        _authorizationRequests.Object, TestHelpers.CreateOptions(_idpSettings), _clock,
        new Mock<ILogger<OrganizationSetupSession>>().Object);

    private SetUpOrganizationCommandHandler Handler() => new(
        Session(), _organizations.Object, _roles.Object, _publisher.Object,
        TestHelpers.CreateOptions(_settings), new Mock<ILogger<SetUpOrganizationCommandHandler>>().Object);

    private GetOrganizationSetupQueryHandler QueryHandler() => new(
        Session(), _organizations.Object, TestHelpers.CreateOptions(_settings));

    private List<OrganizationProvisioningRequest> ProvisionAnswers(params OrganizationProvisioningOutcome[] outcomes)
    {
        var requests = new List<OrganizationProvisioningRequest>();
        var queue = new Queue<OrganizationProvisioningOutcome>(outcomes);
        _organizations
            .Setup(r => r.ProvisionForApplicationAsync(It.IsAny<OrganizationProvisioningRequest>(), It.IsAny<CancellationToken>()))
            .Callback((OrganizationProvisioningRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                _calls.Add("commit");
            })
            .ReturnsAsync(() => queue.Dequeue());
        return requests;
    }

    private static SetUpOrganizationCommand Create(string? token = IdpToken, string? clientId = ClientId, string name = "شركة المتجر") =>
        new(token, clientId, name, null);

    private void VerifyNothingWritten() =>
        _organizations.Verify(
            r => r.ProvisionForApplicationAsync(It.IsAny<OrganizationProvisioningRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-cookie")]
    public async Task WithoutAUsableSession_TheAnswerIsSignIn_AndNothingIsWritten(string? token)
    {
        var result = await Handler().Handle(Create(token: token), CancellationToken.None);

        result.Value.SignInRequired.Should().BeTrue();
        VerifyNothingWritten();
    }

    [Fact]
    public async Task ARevokedSession_IsNoSession()
    {
        var session = new IdpSession(
            Guid.NewGuid(), _user.Id, "idp-hash", DateTime.UtcNow, DateTime.UtcNow.AddDays(7), null, null, null);
        session.Revoke();
        _idpSessions.Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.Value.SignInRequired.Should().BeTrue();
        VerifyNothingWritten();
    }

    [Theory]
    [InlineData("unknown client")]
    [InlineData("not allowed")]
    [InlineData("restricted")]
    [InlineData("inactive application")]
    [InlineData("role of another application")]
    public async Task AnApplicationNotOffering_IsCreationFromApplicationUnavailable(string unavailable)
    {
        var clientId = ClientId;
        switch (unavailable)
        {
            case "unknown client": clientId = "NOPE"; break;
            case "not allowed": _application.LoadOrganizationCreation(false, _creatorRoleId); break;
            case "restricted":
                _application.Update(_application.Name, null, null, null, null, null, false, false, false, 60, 5,
                    ApplicationAccessMode.Restricted, Guid.NewGuid());
                break;
            case "inactive application": _application.Deactivate(Guid.NewGuid()); break;
            case "role of another application":
                _roles.Setup(r => r.GetByIdAsync(_creatorRoleId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateRole(id: _creatorRoleId, applicationId: Guid.NewGuid()));
                break;
        }

        var result = await Handler().Handle(Create(clientId: clientId), CancellationToken.None);

        result.FirstError.Code.Should().Be("Organization.CreationFromApplicationUnavailable");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task AnUnprovenEmail_IsRefused_BeforeAnyOrganizationExists()
    {
        SignIn(TestHelpers.CreateUser(emailConfirmed: false));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.EmailNotConfirmed");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Create_SendsOneRequest_WithAnAsciiCode_TheUsersEmail_AndTheConfiguredRoles()
    {
        _settings.MaxSelfServiceOrganizationsPerUser = 3;
        var requests = ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var result = await Handler().Handle(Create(name: "  شركة المتجر  "), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var request = requests.Should().ContainSingle().Subject;
        request.NewOrganization!.Code.Should().MatchRegex("^org-[a-z2-7]{10}$",
            "an ASCII code that never comes from the (possibly Arabic) name");
        request.NewOrganization.Name.Should().Be("شركة المتجر");
        request.NewOrganization.IsAutoCreated.Should().BeFalse();
        request.NewOrganization.OwnerId.Should().Be(_user.Id);
        request.NewOrganization.ContactEmail.Value.Should().Be(_user.Email.Value);
        request.UserId.Should().Be(_user.Id);
        request.ApplicationId.Should().Be(_application.Id);
        request.CreatorRoleId.Should().Be(_creatorRoleId);
        request.OwnerRoleId.Should().Be(_ownerRoleId);
        request.MaxSelfServiceOrganizations.Should().Be(3);
        request.ExistingOrganizationId.Should().BeNull();
    }

    [Fact]
    public void GeneratedCodes_PassTheConsolesOwnCodeRule()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => SetUpOrganizationCommandHandler.GenerateCode()).ToList();

        codes.Should().HaveCount(200).And.OnlyContain(code =>
            System.Text.RegularExpressions.Regex.IsMatch(code, "^org-[a-z2-7]{10}$") && code.Length <= 50);
        codes.Distinct().Should().HaveCount(200);
    }

    [Fact]
    public async Task Provisioned_PublishesTheEventOnce_AfterTheCommit()
    {
        var organizationId = Guid.NewGuid();
        ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, organizationId, true));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.Value.OrganizationId.Should().Be(organizationId);
        _calls.Should().Equal(["commit", "publish"]);
        _publisher.Verify(p => p.Publish(
                It.Is<OrganizationProvisionedForApplicationEvent>(e =>
                    e.OrganizationId == organizationId && e.UserId == _user.Id
                    && e.ApplicationId == _application.Id && e.CreatorRoleId == _creatorRoleId
                    && e.OrganizationCreated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ThePlatformSwitchOff_DoesNotStopCreationFromAnApplication()
    {
        // Owner decision D-46-1 (b), 2026-10-05: Organizations:AllowSelfServiceCreation
        // governs the console's and the accounts app's "Create organization" only.
        // Creation from an application is governed by that application's own
        // setting. Pinned so that changing it is a deliberate decision, not a
        // side effect.
        _settings.AllowSelfServiceCreation = false;
        _organizations.Setup(r => r.CountSelfServiceOwnedAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _organizations
            .Setup(r => r.GetOrganizationSetupCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var requests = ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var state = await QueryHandler().Handle(new GetOrganizationSetupQuery(IdpToken, ClientId), CancellationToken.None);
        var result = await Handler().Handle(Create(), CancellationToken.None);

        state.Value.State!.CanCreate.Should().BeTrue("the switch does not close the application path");
        result.IsError.Should().BeFalse("the switch does not close the application path");
        requests.Should().ContainSingle().Which.NewOrganization.Should().NotBeNull();
    }

    [Fact]
    public async Task WithShippedDefaults_TheApplicationPathStillProvisions()
    {
        // OI-78: the platform switch now ships closed. Nothing is set here, so this
        // is a fresh deployment with no override: the application path provisions.
        _settings.AllowSelfServiceCreation.Should().BeFalse("the shipped default is closed");
        _organizations.Setup(r => r.CountSelfServiceOwnedAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _organizations
            .Setup(r => r.GetOrganizationSetupCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var requests = ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        requests.Should().ContainSingle().Which.NewOrganization.Should().NotBeNull();
    }

    [Fact]
    public async Task ACodeCollision_RetriesWithAnotherCode()
    {
        var requests = ProvisionAnswers(
            new(OrganizationProvisioningStatus.CodeTaken),
            new(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        requests.Should().HaveCount(2);
        requests[0].NewOrganization!.Code.Should().NotBe(requests[1].NewOrganization!.Code);
    }

    [Fact]
    public async Task AtTheLimit_TheAnswerIsSelfServiceLimitReached_AndNothingIsPublished()
    {
        ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.LimitReached));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.FirstError.Code.Should().Be("Organization.SelfServiceLimitReached");
        _calls.Should().NotContain("publish");
    }

    [Fact]
    public async Task ASecondCall_ReturnsTheSameOrganization_AndPublishesNothing()
    {
        var organizationId = Guid.NewGuid();
        ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.AlreadySetUp, organizationId));

        var result = await Handler().Handle(Create(), CancellationToken.None);

        result.Value.SignInRequired.Should().BeFalse();
        result.Value.OrganizationId.Should().Be(organizationId);
        _calls.Should().NotContain("publish");
    }

    [Fact]
    public async Task UseExisting_AsksForThatOrganization_WithoutCreatingOne()
    {
        var organizationId = Guid.NewGuid();
        var requests = ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, organizationId, false));

        var result = await Handler().Handle(new SetUpOrganizationCommand(IdpToken, ClientId, null, organizationId), CancellationToken.None);

        result.Value.OrganizationId.Should().Be(organizationId);
        var request = requests.Should().ContainSingle().Subject;
        request.ExistingOrganizationId.Should().Be(organizationId);
        request.NewOrganization.Should().BeNull();
        request.UserId.Should().Be(_user.Id);
        _publisher.Verify(p => p.Publish(
            It.Is<OrganizationProvisionedForApplicationEvent>(e => !e.OrganizationCreated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseExisting_SomeoneElsesOrganization_ReadsAsNotFound()
    {
        var organizationId = Guid.NewGuid();
        ProvisionAnswers(new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.OrganizationNotEligible));

        var result = await Handler().Handle(new SetUpOrganizationCommand(IdpToken, ClientId, null, organizationId), CancellationToken.None);

        result.FirstError.Code.Should().Be("Organization.NotFound");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
        _calls.Should().NotContain("publish");
    }

    [Fact]
    public async Task ScreenState_ReportsTheLimit_AndTheOrganizationsThatCouldBeSetUp()
    {
        var owned = Guid.NewGuid();
        _settings.MaxSelfServiceOrganizationsPerUser = 1;
        _organizations.Setup(r => r.CountSelfServiceOwnedAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _organizations
            .Setup(r => r.GetOrganizationSetupCandidatesAsync(_user.Id, _application.Id, _creatorRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OrganizationSetupCandidate(owned, "مؤسسة قائمة")]);

        var result = await QueryHandler().Handle(new GetOrganizationSetupQuery(IdpToken, ClientId), CancellationToken.None);

        var state = result.Value.State!;
        state.CanCreate.Should().BeFalse("one owned self-service organization at a limit of one");
        state.Limit.Should().Be(1);
        state.OwnedOrganizations.Should().ContainSingle().Which.Should().Be(new OrganizationSetupOption(owned, "مؤسسة قائمة"));
        state.Email.Should().Be(_user.Email.Value, "the page names the account the session belongs to");
        state.EmailConfirmed.Should().BeTrue();

        // Evidence for the PR: the body the accounts page receives.
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"canCreate\":false").And.Contain("\"limit\":1").And.Contain("\"ownedOrganizations\":[{\"id\":");
    }

    [Fact]
    public async Task ScreenState_ForAnUnprovenAddress_OffersNothingButSaysWhy()
    {
        // Not a refusal: the page needs the address to send the user to confirm
        // it, and an error would leave them on a page with nothing to act on.
        SignIn(TestHelpers.CreateUser(emailConfirmed: false));

        var result = await QueryHandler().Handle(new GetOrganizationSetupQuery(IdpToken, ClientId), CancellationToken.None);

        var state = result.Value.State!;
        state.EmailConfirmed.Should().BeFalse();
        state.CanCreate.Should().BeFalse();
        state.OwnedOrganizations.Should().BeEmpty();
        state.Email.Should().Be(_user.Email.Value);
        _organizations.Verify(
            r => r.GetOrganizationSetupCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScreenState_WithoutASession_IsSignIn()
    {
        var result = await QueryHandler().Handle(new GetOrganizationSetupQuery(null, ClientId), CancellationToken.None);

        result.Value.SignInRequired.Should().BeTrue();
        _organizations.Verify(
            r => r.CountSelfServiceOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScreenState_BelowTheLimit_CanCreate()
    {
        _settings.MaxSelfServiceOrganizationsPerUser = 1;
        _organizations.Setup(r => r.CountSelfServiceOwnedAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _organizations
            .Setup(r => r.GetOrganizationSetupCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await QueryHandler().Handle(new GetOrganizationSetupQuery(IdpToken, ClientId), CancellationToken.None);

        result.Value.State!.CanCreate.Should().BeTrue();
        result.Value.State.OwnedOrganizations.Should().BeEmpty();
    }

    [Fact]
    public void TheValidator_RequiresANameOnlyWhenCreating()
    {
        var validator = new SetUpOrganizationCommandValidator();

        validator.Validate(new SetUpOrganizationCommand(IdpToken, ClientId, " ", null)).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("Organization.NameRequired");
        validator.Validate(new SetUpOrganizationCommand(IdpToken, ClientId, new string('x', 201), null)).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("Organization.NameTooLong");
        validator.Validate(new SetUpOrganizationCommand(IdpToken, ClientId, null, Guid.NewGuid())).IsValid
            .Should().BeTrue();
    }

    #region OI-150: the organization step obeys the finish window of the request it serves

    private static readonly string Handle = new('a', 64);
    private readonly Mock<IAuthorizationRequestRepository> _authorizationRequests = new();
    private readonly IdentityProviderSettings _idpSettings = new();
    private readonly AdjustableTimeProvider _clock = new();

    /// <summary>
    /// The request the page serves, stored by authorize under <see cref="Handle"/>
    /// with <paramref name="prompt"/>, and the sign-in answering it,
    /// <paramref name="minutesSinceSignIn"/> old.
    /// </summary>
    private void SetupStoredRequest(string? prompt, int minutesSinceSignIn, string clientId = ClientId)
    {
        _clock.Advance(TimeSpan.Zero);
        var signedInAt = _clock.UtcNow.AddMinutes(-minutesSinceSignIn);
        _idpSessions
            .Setup(r => r.GetByTokenHashAsync("idp-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdpSession(
                Guid.NewGuid(), _user.Id, "idp-hash", signedInAt, signedInAt.AddDays(7), null, null, null));
        _keys.Setup(k => k.ComputeTokenHash(Handle)).Returns("handle-hash");
        _authorizationRequests
            .Setup(r => r.GetLiveByHandleHashAsync("handle-hash", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationRequest.Create(
                "handle-hash", _application.Id, clientId, "code", "https://app.example.com/callback",
                new string('c', 43), "S256", "xyz", prompt, null, "openid", createOrganization: true,
                TimeSpan.FromMinutes(60), signedInAt.AddMinutes(-1)));
    }

    private void SetupScreenState()
    {
        _organizations.Setup(r => r.CountSelfServiceOwnedAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _organizations
            .Setup(r => r.GetOrganizationSetupCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OrganizationSetupCandidate(Guid.NewGuid(), "مؤسسة قائمة")]);
    }

    private static SetUpOrganizationCommand CreateFor(string? handle) =>
        new(IdpToken, ClientId, "مؤسسة تجريبية", null, handle);

    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public async Task Step_ForARequestThatDemandedSignIn_CreatesOnlyInsideTheFinishWindow(int minutesSinceSignIn, bool inside)
    {
        SetupStoredRequest("login create", minutesSinceSignIn);
        var requests = ProvisionAnswers(
            new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var result = await Handler().Handle(CreateFor(Handle), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.SignInRequired.Should().Be(!inside);
        if (inside)
        {
            requests.Should().ContainSingle();
        }
        else
        {
            VerifyNothingWritten();
            _publisher.Verify(
                p => p.Publish(It.IsAny<OrganizationProvisionedForApplicationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public async Task ScreenState_ForARequestThatDemandedSignIn_ListsOnlyInsideTheFinishWindow(int minutesSinceSignIn, bool inside)
    {
        SetupStoredRequest("login", minutesSinceSignIn);
        SetupScreenState();

        var result = await QueryHandler().Handle(
            new GetOrganizationSetupQuery(IdpToken, ClientId, Handle), CancellationToken.None);

        result.Value.SignInRequired.Should().Be(!inside);
        if (inside)
        {
            result.Value.State!.OwnedOrganizations.Should().ContainSingle();
        }
        else
        {
            result.Value.State.Should().BeNull("the person's organizations are not listed to whoever is at the keyboard");
        }
    }

    /// <summary>
    /// Every handle that does not name a live request of this application that
    /// demanded a fresh sign-in: the step answers as it always has.
    /// </summary>
    public static TheoryData<string> AsWithoutAHandle => new()
    {
        "no handle", "malformed handle", "unknown handle", "another application's request", "a request without login",
    };

    [Fact]
    public void AsWithoutAHandle_IsNotEmpty() => AsWithoutAHandle.Count.Should().BeGreaterThan(0);

    private string? ArrangeAsWithoutAHandle(string name)
    {
        // A sign-in six minutes old: outside the window, so only the absence of
        // a usable handle lets the step answer.
        switch (name)
        {
            case "another application's request":
                SetupStoredRequest("login", 6, clientId: "OTHER");
                return Handle;
            case "a request without login":
                SetupStoredRequest("create", 6);
                return Handle;
            default:
                SetupStoredRequest("login", 6);
                return name switch
                {
                    "no handle" => null,
                    "malformed handle" => Handle.ToUpperInvariant(),
                    "unknown handle" => new string('b', 64),
                    _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
                };
        }
    }

    [Theory]
    [MemberData(nameof(AsWithoutAHandle))]
    public async Task Step_WithoutAUsableHandle_AnswersAsBefore(string name)
    {
        var handle = ArrangeAsWithoutAHandle(name);
        var requests = ProvisionAnswers(
            new OrganizationProvisioningOutcome(OrganizationProvisioningStatus.Provisioned, Guid.NewGuid(), true));

        var result = await Handler().Handle(CreateFor(handle), CancellationToken.None);

        result.Value.SignInRequired.Should().BeFalse();
        requests.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(AsWithoutAHandle))]
    public async Task ScreenState_WithoutAUsableHandle_AnswersAsBefore(string name)
    {
        var handle = ArrangeAsWithoutAHandle(name);
        SetupScreenState();

        var result = await QueryHandler().Handle(
            new GetOrganizationSetupQuery(IdpToken, ClientId, handle), CancellationToken.None);

        result.Value.SignInRequired.Should().BeFalse();
        result.Value.State!.OwnedOrganizations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("AAAA")]
    [InlineData("trailing newline")]
    public async Task AMalformedHandle_NeverReachesTheStore(string shape)
    {
        SetupStoredRequest("login", 6);
        SetupScreenState();
        var malformed =shape == "trailing newline" ? Handle + "\n" : shape;

        await QueryHandler().Handle(
            new GetOrganizationSetupQuery(IdpToken, ClientId, malformed), CancellationToken.None);

        _authorizationRequests.Verify(
            r => r.GetLiveByHandleHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
