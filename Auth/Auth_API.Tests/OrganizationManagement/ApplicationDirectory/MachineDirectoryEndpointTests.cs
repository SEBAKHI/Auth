using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Behaviors;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Domain.ValueObjects;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Authorization;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// The machine directory (OI-202) as a relying party's server meets it: the REAL
/// <see cref="ApplicationMachineOrganizationsController"/> beside the REAL user door
/// <see cref="ApplicationOrganizationsController"/>, the SAME scheme registration Program.cs calls,
/// the real blacklist middleware in Program.cs's position, the real handlers and
/// <see cref="ApplicationDirectoryAccess"/> behind a real mediator with the validation behaviour,
/// and stub repositories. Every token is minted by the real token service. What is asserted is the
/// wire: status, headers, problem code and JSON members.
/// </summary>
public sealed class MachineDirectoryEndpointTests : IAsyncLifetime
{
    private const string AppCode = UserInfoTokens.Application;
    private const string Challenge = "Bearer error=\"insufficient_scope\", scope=\"organizations.read.all\"";

    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IImageUrlComposer> _images = new();

    private readonly User _member = TestHelpers.CreateUser(email: "agent@example.com", firstName: "Ada", lastName: "Agent");
    private readonly AppEntity _application;
    private readonly Organization _organization;
    private readonly Guid _agentRoleId = Guid.NewGuid();
    private readonly Guid _memberRoleId = Guid.NewGuid();
    private InvitationListFilter? _invitationFilter;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public MachineDirectoryEndpointTests()
    {
        _application = TestHelpers.CreateApplication(code: AppCode);
        _organization = TestHelpers.CreateOrganization(name: "Example Org", logoUrl: "logos/org.png");

        _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_organization);
        _organizations.Setup(r => r.IsApplicationEnabledAsync(_organization.Id, _application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _organizations
            .Setup(r => r.GetActiveMembersHoldingApplicationRolePagedAsync(
                _organization.Id, _application.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([
                new ApplicationDirectoryMemberRow(Guid.NewGuid(), _member.Id, "Ada", "Agent", "agent@example.com", "avatars/ada.png", "org-member", Now.AddDays(-30)),
            ], 1));
        _organizations
            .Setup(r => r.GetMemberApplicationRolesAsync(_organization.Id, _application.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MemberApplicationRoleRow(_member.Id, "agent")]);
        _organizations
            .Setup(r => r.GetInvitationsPagedAsync(_organization.Id, It.IsAny<InvitationListFilter>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, InvitationListFilter filter, DateTime _, int _, int _, string? _, SortDirection _, CancellationToken _) => _invitationFilter = filter)
            .ReturnsAsync(() => ([
                Invitation("mine@example.com", _application.Id),
                // The query's filter guarantees these never come back; the handler drops them too.
                Invitation("other-app@example.com", Guid.NewGuid()),
                Invitation("membership-only@example.com", applicationId: null),
            ], 1));
        _roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => TestHelpers.CreateRole(id: id, code: id == _memberRoleId ? "org-member" : "agent"));

        _images.Setup(c => c.Compose("logos/org.png")).Returns("https://auth.example.com/uploads/images/logos/org.png");
        _images.Setup(c => c.Compose("avatars/ada.png")).Returns("https://auth.example.com/uploads/images/avatars/ada.png");
    }

    private OrganizationInvitation Invitation(string email, Guid? applicationId) =>
        TestHelpers.CreateOrganizationInvitation(
            organizationId: _organization.Id,
            email: email,
            roleId: _memberRoleId,
            expiresAt: Now.AddDays(6),
            createdAt: Now.AddDays(-1),
            applicationRole: InvitationApplicationRole.FromNullable(applicationId, applicationId is null ? null : _agentRoleId));

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddLogging();
                    services.Configure<IdentityProviderSettings>(_ => { });

                    // The registrations Program.cs makes for this path.
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
                    services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
                    services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaForbiddenResultHandler>();
                    services.AddAuthorization();
                    services.AddMediatR(cfg =>
                    {
                        cfg.RegisterServicesFromAssemblyContaining<GetApplicationOrganizationQueryHandler>();
                        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    });
                    services.AddValidatorsFromAssemblyContaining<ApplicationDirectoryAccess>();
                    services.AddScoped<ApplicationDirectoryAccess>();
                    services.AddSingleton(_users.Object);
                    services.AddSingleton(_applications.Object);
                    services.AddSingleton(_organizations.Object);
                    services.AddSingleton(_roles.Object);
                    services.AddSingleton(_images.Object);
                    services.AddSingleton(_blacklist.Object);
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(Now)));
                    services.AddRateLimiter(options => options.AddPolicy(ApplicationReadClassificationMiddleware.PolicyName,
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("unlimited")));

                    services.AddControllers()
                        .AddJsonOptions(options =>
                        {
                            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                        })
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new TheseControllersOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        options.ReportApiVersions = true;
                        options.ApiVersionReader = new UrlSegmentApiVersionReader();
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    // Program.cs's order: error contract, authentication, classification, limiter,
                    // blacklist, authorization.
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<ApplicationReadClassificationMiddleware>();
                    app.UseRateLimiter();
                    app.UseMiddleware<JwtBlacklistValidationMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _tokens.Dispose();
    }

    #region Helpers

    private string MachinePath(string suffix = "", Guid? organizationId = null) =>
        $"/api/v1/app/machine/organizations/{organizationId ?? _organization.Id}{suffix}";

    private string UserPath(string suffix = "") => $"/api/v1/app/organizations/{_organization.Id}{suffix}";

    private string MachineToken(params string[] scopes) =>
        _tokens.Service.GenerateMachineAccessToken(AppCode, scopes.Length == 0 ? [MachineClients.Scopes.OrganizationsReadAll] : scopes);

    private string UserToken(string scope) =>
        _tokens.ForApplication(_member, scope, audience: AppCode);

    private async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string WwwAuthenticate(HttpResponseMessage response) =>
        string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

    private static IReadOnlyList<string> Members(JsonElement body) =>
        body.EnumerateObject().Select(p => p.Name).ToList();

    private static IReadOnlyList<string> Headers(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not "Date" and not "Content-Length")
            .Select(h => $"{h.Key.ToLowerInvariant()}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response)
    {
        var body = await JsonAsync(response);
        return JsonSerializer.Serialize(body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.ToString()));
    }

    private void NoUserWasEverRead()
    {
        _users.VerifyNoOtherCalls();
        _organizations.Verify(r => r.GetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _organizations.Verify(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    // --- T5: the three reads, the same answers as the user door ---

    [Fact]
    public async Task Organization_MachineTokenWithTheScope_Returns200_WithIdNameAndAbsoluteLogo_NoStore()
    {
        var response = await GetAsync(MachinePath(), MachineToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(response);
        Members(body).Should().BeEquivalentTo("id", "name", "logoUrl");
        body.GetProperty("id").GetGuid().Should().Be(_organization.Id);
        body.GetProperty("logoUrl").GetString().Should().Be("https://auth.example.com/uploads/images/logos/org.png");
        NoUserWasEverRead();
    }

    [Fact]
    public async Task Members_AreTheHoldersOfThisApplicationsRoles_AlwaysWithThoseRoles()
    {
        var response = await GetAsync(MachinePath("/members?pageSize=2"), MachineToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(response);
        Members(body).Should().BeEquivalentTo("items", "totalCount", "pageNumber", "pageSize");
        body.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("userId").GetGuid().Should().Be(_member.Id);
        item.GetProperty("applicationRoles").EnumerateArray().Select(r => r.GetString()).Should().Equal("agent");

        // D-202-3 (a): the narrowed query, never the whole membership list.
        _organizations.Verify(r => r.GetActiveMembersHoldingApplicationRolePagedAsync(
            _organization.Id, _application.Id, Now, 1, 2, It.IsAny<CancellationToken>()), Times.Once);
        _organizations.Verify(r => r.GetActiveMembersPagedAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        NoUserWasEverRead();
    }

    [Fact]
    public async Task Invitations_AreThisApplicationsOnly()
    {
        var response = await GetAsync(MachinePath("/invitations"), MachineToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _invitationFilter!.ApplicationId.Should().Be(_application.Id, "the query is narrowed to this application's role");
        var items = (await JsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("email").GetString()).Should().Equal("mine@example.com");
        items.Single().GetProperty("applicationRole").GetString().Should().Be("agent");
        NoUserWasEverRead();
    }

    [Fact]
    public async Task ABadQueryValue_IsTheSame400_AsTheUserDoor()
    {
        var response = await GetAsync(MachinePath("/members?pageSize=0"), MachineToken());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(PagingErrors.PageSizeOutOfRange.Code);
    }

    // --- V1: who may call at all ---

    public static TheoryData<string> Paths => new() { "", "/members", "/invitations" };

    [Fact]
    public void Paths_IsNotEmpty() => Paths.Count<object[]>().Should().Be(3);

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task PlatformToken_Returns401(string suffix)
    {
        var response = await GetAsync(MachinePath(suffix), _tokens.ForPlatform(_member));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AUsersApplicationToken_Returns401_EvenNamingTheMachineScope(string suffix)
    {
        var response = await GetAsync(MachinePath(suffix), UserToken("openid organizations.read organizations.read.all"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AMachineToken_OnTheUserDoor_Returns401(string suffix)
    {
        var response = await GetAsync(UserPath(suffix), MachineToken());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AMachineTokenWithoutTheScope_Returns403_NamingTheMachineScope(string suffix)
    {
        var response = await GetAsync(MachinePath(suffix), MachineToken(MachineClients.Scopes.All.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Forbidden);
        WwwAuthenticate(response).Should().Be(Challenge);
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task TheScopeInTheToken_ButTheConsentWithdrawn_Returns403AtTheNextCall(string suffix)
    {
        var token = MachineToken();
        (await GetAsync(MachinePath(suffix), token)).StatusCode.Should().Be(HttpStatusCode.OK);

        _application.SetMachineOrganizationsRead(false, Guid.NewGuid()).Should().BeTrue();
        var response = await GetAsync(MachinePath(suffix), token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the live token keeps nothing once the consent is withdrawn");
        WwwAuthenticate(response).Should().Be(Challenge);
    }

    public static TheoryData<string> UnservableApplications => new() { "inactive application", "deleted application" };

    [Fact]
    public void UnservableApplications_IsNotEmpty() => UnservableApplications.Count<object[]>().Should().Be(2);

    [Theory]
    [MemberData(nameof(UnservableApplications))]
    public async Task AnUnservableApplication_Returns401_IdenticalToARevokedToken(string reason)
    {
        _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reason == "inactive application"
                ? TestHelpers.CreateApplication(id: _application.Id, code: AppCode, isActive: false)
                : null);

        var revokedToken = MachineToken();
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revokedToken)!)).Returns(true);

        var unservable = await GetAsync(MachinePath("/members"), MachineToken());
        var revoked = await GetAsync(MachinePath("/members"), revokedToken);

        unservable.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(unservable).Should().Be("Bearer error=\"invalid_token\"");
        (await CodeAsync(unservable)).Should().Be(ChallengeReasonCodes.TokenRevoked);
        Headers(unservable).Should().Equal(Headers(revoked), "one 401 is written in MVC, the other before it");
        (await BodyWithoutTraceAsync(unservable)).Should().Be(await BodyWithoutTraceAsync(revoked));
    }

    // --- V3: one 404 for every "not yours" ---

    public static TheoryData<string> NotYours => new()
    {
        "unknown organization", "inactive organization", "application not enabled (or lapsed)",
    };

    [Fact]
    public void NotYours_IsNotEmpty() => NotYours.Count<object[]>().Should().Be(3);

    [Theory]
    [MemberData(nameof(NotYours))]
    public async Task EveryNotYours_IsTheSame404_AsAnOrganizationThatDoesNotExist(string reason)
    {
        var unknownOrganization = Guid.NewGuid();
        var unknown = await GetAsync(MachinePath("/members", unknownOrganization), MachineToken());

        switch (reason)
        {
            case "unknown organization":
                _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);
                break;
            case "inactive organization":
                _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateOrganization(id: _organization.Id, isActive: false));
                break;
            default:
                // IsApplicationEnabledAsync answers false for no row, an inactive row and an expired one.
                _organizations.Setup(r => r.IsApplicationEnabledAsync(_organization.Id, _application.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);
                break;
        }

        var notYours = await GetAsync(MachinePath("/members"), MachineToken());

        notYours.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(notYours)).Should().Be("Organization.NotFound");
        Headers(notYours).Should().Equal(Headers(unknown));
        (await BodyWithoutTraceAsync(notYours))
            .Should().Be((await BodyWithoutTraceAsync(unknown)).Replace(unknownOrganization.ToString(), _organization.Id.ToString()),
                "the same body but for the organization id the caller sent");
    }

    // --- F4 (a): the doors expose their three reads and nothing inherited ---

    [Fact]
    public void TheSharedBase_DeclaresNoPublicMethod()
    {
        typeof(ApplicationDirectoryController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Should().BeEmpty("a public method here would be an action on both doors at once");
    }

    [Fact]
    public void EachDoor_IsExactlyItsThreeReads_AsRoutingSeesThem()
    {
        // The actions MVC actually discovered, inherited ones included.
        var actions = _host.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .ToList();

        foreach (var door in new[] { typeof(ApplicationMachineOrganizationsController), typeof(ApplicationOrganizationsController) })
        {
            actions.Where(action => action.ControllerTypeInfo.AsType() == door)
                .Select(action => $"{action.ActionName} {action.AttributeRouteInfo?.Template}")
                .Should().HaveCount(3, "{0} exposes the organization, its members and its invitations, and nothing else", door.Name)
                .And.OnlyContain(route => route.StartsWith("GetOrganization ", StringComparison.Ordinal)
                    || route.StartsWith("GetMembers ", StringComparison.Ordinal)
                    || route.StartsWith("GetInvitations ", StringComparison.Ordinal));
        }
    }

    // --- V5: no user ever stands behind a machine read ---

    [Fact]
    public async Task AHandSignedMachineToken_WhoseClientIdIsAUsersId_IsRefusedByTheScheme()
    {
        // Signed with the real key: only the scheme's rule (client_id not shaped like a GUID)
        // stands between a member's id and a machine read.
        var forged = _tokens.Custom(_member.Id, descriptor =>
        {
            descriptor.Audience = MachineClients.Audience;
            descriptor.TokenType = "at+jwt";
            descriptor.Expires = DateTime.UtcNow.AddMinutes(5);
            descriptor.Claims = new Dictionary<string, object>
            {
                ["sub"] = _member.Id.ToString(),
                ["client_id"] = _member.Id.ToString(),
                ["jti"] = Guid.NewGuid().ToString(),
                ["scope"] = MachineClients.Scopes.OrganizationsReadAll,
            };
        });

        var response = await GetAsync(MachinePath("/members"), forged);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _applications.Verify(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        NoUserWasEverRead();
    }

    [Fact]
    public async Task TheMachineGrant_CarriesNoUserId()
    {
        var access = new ApplicationDirectoryAccess(_users.Object, _applications.Object, _organizations.Object);

        var grant = await access.ResolveAsync(
            new ApplicationMachineCaller(AppCode, MachineClients.Scopes.OrganizationsReadAll), _organization.Id, null, CancellationToken.None);

        grant.IsError.Should().BeFalse();
        grant.Value.Authority.Should().BeOfType<ApplicationDirectoryMachineAuthority>();
        grant.Value.Authority.CallerKind.Should().Be("machine");
        typeof(ApplicationDirectoryMachineAuthority).GetProperties().Select(p => p.PropertyType)
            .Should().NotContain(typeof(Guid), "a machine read names no person");
        NoUserWasEverRead();
    }

    private sealed class TheseControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(ApplicationMachineOrganizationsController).GetTypeInfo());
            feature.Controllers.Add(typeof(ApplicationOrganizationsController).GetTypeInfo());
        }
    }
}
