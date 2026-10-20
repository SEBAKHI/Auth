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
using Microsoft.IdentityModel.Tokens;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// The application directory (OI-194) as a relying party's server meets it: the REAL
/// <see cref="ApplicationOrganizationsController"/>, the SAME scheme registration Program.cs
/// calls, the real blacklist middleware in Program.cs's position, the real handlers and
/// <see cref="ApplicationDirectoryAccess"/> behind a real mediator with the validation behaviour,
/// and stub repositories. Every token is minted by the real token service. What is asserted is
/// the wire: status, headers, problem code and JSON members.
/// </summary>
public sealed class ApplicationDirectoryEndpointTests : IAsyncLifetime
{
    private const string AppCode = UserInfoTokens.Application;
    private const string Scope = "openid profile email organizations.read";

    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IImageUrlComposer> _images = new();

    private readonly User _user = TestHelpers.CreateUser(email: "member@example.com", firstName: "Mia", lastName: "Member");
    private readonly AppEntity _application;
    private readonly Organization _organization;
    private readonly Guid _memberRoleId = Guid.NewGuid();
    private List<string> _membershipCodes = [PermissionCodes.Org.MembersRead];

    private IHost _host = null!;
    private HttpClient _client = null!;

    public ApplicationDirectoryEndpointTests()
    {
        _application = AllowedTheScope(TestHelpers.CreateApplication(code: AppCode));
        _organization = TestHelpers.CreateOrganization(name: "Example Org", logoUrl: "logos/org.png");

        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_organization);
        _organizations.Setup(r => r.IsApplicationEnabledAsync(_organization.Id, _application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: _organization.Id, userId: _user.Id));
        _organizations.Setup(r => r.GetMembershipPermissionCodesAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _membershipCodes);
        _organizations
            .Setup(r => r.GetActiveMembersPagedAsync(_organization.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([
                new ApplicationDirectoryMemberRow(Guid.NewGuid(), _user.Id, "Mia", "Member", "member@example.com", "avatars/mia.png", "org-member", Now.AddDays(-30)),
                new ApplicationDirectoryMemberRow(Guid.NewGuid(), Guid.NewGuid(), null, null, "nameless@example.com", null, "org-admin", Now.AddDays(-3)),
            ], 2));
        _organizations
            .Setup(r => r.GetMemberApplicationRolesAsync(_organization.Id, _application.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MemberApplicationRoleRow(_user.Id, "agent")]);
        _organizations
            .Setup(r => r.GetInvitationsPagedAsync(_organization.Id, It.IsAny<InvitationListFilter>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));
        _roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => TestHelpers.CreateRole(id: id, code: id == _memberRoleId ? "org-member" : "agent"));

        _images.Setup(c => c.Compose("logos/org.png")).Returns("https://auth.example.com/uploads/images/logos/org.png");
        _images.Setup(c => c.Compose("avatars/mia.png")).Returns("https://auth.example.com/uploads/images/avatars/mia.png");
    }

    private static AppEntity AllowedTheScope(AppEntity application)
    {
        application.SetAllowedScopes([OAuthScopes.Profile, OAuthScopes.Email, OAuthScopes.OrganizationsRead], Guid.Empty)
            .IsError.Should().BeFalse();
        return application;
    }

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
                    // Every validator of the application layer, as Program.cs registers them: none may run ahead
                    // of the caller check for these queries.
                    services.AddValidatorsFromAssemblyContaining<ApplicationDirectoryAccess>();
                    services.AddScoped<ApplicationDirectoryAccess>();
                    services.AddSingleton(_users.Object);
                    services.AddSingleton(_applications.Object);
                    services.AddSingleton(_organizations.Object);
                    services.AddSingleton(_roles.Object);
                    services.AddSingleton(_images.Object);
                    services.AddSingleton(_blacklist.Object);
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(Now)));
                    // The controller opts into the "app-read" policy; the budget has its own tests.
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

    private string OrgPath(string suffix = "") => $"/api/v1/app/organizations/{_organization.Id}{suffix}";

    private string Token(User? user = null, string? scope = Scope, IEnumerable<string>? permissions = null,
        IEnumerable<(Guid, string)>? orgPermissions = null, Guid? sessionId = null) =>
        _tokens.Service.GenerateAccessToken(
            user ?? _user,
            permissions: permissions ?? [$"{AppCode}:customers:read"],
            roles: ["customer"],
            authentication: Auth.Application.Interfaces.AccessTokenAuthentication.Unrecorded,
            sessionId: sessionId ?? Guid.NewGuid(),
            organizationPermissions: orgPermissions ?? [(_organization.Id, PermissionCodes.Org.MembersRead)],
            audience: AppCode,
            scope: scope);

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

    /// <summary>Every header name and value but the ones that differ per response by nature.</summary>
    private static IReadOnlyList<string> Headers(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not "Date" and not "Content-Length")
            .Select(h => $"{h.Key.ToLowerInvariant()}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The body without traceId, which every response has its own of.</summary>
    private static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response)
    {
        var body = await JsonAsync(response);
        return JsonSerializer.Serialize(body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.ToString()));
    }

    #endregion

    // --- V1: who may call at all ---

    [Fact]
    public async Task Organization_ApplicationTokenWithTheScope_Returns200_WithIdNameAndAbsoluteLogo_NoStore()
    {
        var response = await GetAsync(OrgPath(), Token());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(response);
        Members(body).Should().BeEquivalentTo("id", "name", "logoUrl");
        body.GetProperty("id").GetGuid().Should().Be(_organization.Id);
        body.GetProperty("name").GetString().Should().Be("Example Org");
        body.GetProperty("logoUrl").GetString().Should().Be("https://auth.example.com/uploads/images/logos/org.png");
    }

    public static TheoryData<string> Paths => new() { "", "/members", "/invitations" };

    [Fact]
    public void Paths_IsNotEmpty() => Paths.Count<object[]>().Should().Be(3);

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task PlatformToken_Returns401(string suffix)
    {
        var response = await GetAsync(OrgPath(suffix), _tokens.ForPlatform(_user));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task MachineToken_Returns401(string suffix)
    {
        var machine = _tokens.Custom(Guid.Empty, descriptor =>
        {
            descriptor.Audience = MachineClients.Audience;
            descriptor.TokenType = "at+jwt";
            descriptor.Claims = new Dictionary<string, object>
            {
                ["sub"] = "machine-client",
                ["client_id"] = "machine-client",
                ["jti"] = Guid.NewGuid().ToString(),
                ["scope"] = Scope,
            };
        });

        var response = await GetAsync(OrgPath(suffix), machine);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task ApplicationTokenWithoutTheScope_Returns403_InsufficientScope(string suffix)
    {
        var response = await GetAsync(OrgPath(suffix), Token(scope: "openid profile email"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Forbidden);
        WwwAuthenticate(response).Should().Be("Bearer error=\"insufficient_scope\", scope=\"organizations.read\"");
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScopeInTheToken_ButRemovedFromTheApplication_Returns403AtTheNextCall()
    {
        var token = Token();
        (await GetAsync(OrgPath(), token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // An administrator unticks the scope; the token, minted before, still names it.
        _application.SetAllowedScopes([OAuthScopes.Profile, OAuthScopes.Email], Guid.Empty).IsError.Should().BeFalse();

        var response = await GetAsync(OrgPath(), token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        WwwAuthenticate(response).Should().Contain("insufficient_scope");
    }

    public static TheoryData<string> UnservableCallers => new() { "deleted user", "locked user", "inactive application", "deleted application" };

    [Fact]
    public void UnservableCallers_IsNotEmpty() => UnservableCallers.Count<object[]>().Should().Be(4);

    [Theory]
    [MemberData(nameof(UnservableCallers))]
    public async Task UnservableCaller_Returns401_IdenticalToARevokedToken(string caller)
    {
        switch (caller)
        {
            case "deleted user":
                _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
                break;
            case "locked user":
                _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateUser(id: _user.Id, status: UserStatus.Locked));
                break;
            case "inactive application":
                _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AllowedTheScope(TestHelpers.CreateApplication(id: _application.Id, code: AppCode, isActive: false)));
                break;
            default:
                _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>())).ReturnsAsync((AppEntity?)null);
                break;
        }

        var revokedToken = Token();
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revokedToken)!)).Returns(true);

        var unservable = await GetAsync(OrgPath("/members"), Token());
        var revoked = await GetAsync(OrgPath("/members"), revokedToken);

        unservable.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(unservable).Should().Be("Bearer error=\"invalid_token\"");
        (await CodeAsync(unservable)).Should().Be(ChallengeReasonCodes.TokenRevoked);
        Headers(unservable).Should().Equal(Headers(revoked), "one 401 is written in MVC, the other before it");
        (await BodyWithoutTraceAsync(unservable)).Should().Be(await BodyWithoutTraceAsync(revoked));
    }

    [Fact]
    public async Task TokenOfARevokedSession_Returns401()
    {
        var session = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionBlacklisted(session.ToString())).Returns(true);

        var response = await GetAsync(OrgPath(), Token(sessionId: session));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(ChallengeReasonCodes.SessionRevoked);
    }

    [Fact]
    public async Task TokenInTheQueryString_Returns401()
    {
        var response = await GetAsync($"{OrgPath()}?access_token={Token()}", token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
    }

    // --- V2: one 404 for every "not yours" ---

    public static TheoryData<string> NotYours => new()
    {
        "unknown organization", "inactive organization", "application not enabled (or lapsed)",
        "not a member", "inactive membership", "expired membership",
    };

    [Fact]
    public void NotYours_IsNotEmpty() => NotYours.Count<object[]>().Should().Be(6);

    [Theory]
    [MemberData(nameof(NotYours))]
    public async Task EveryNotYours_IsTheSame404_AsAnOrganizationThatDoesNotExist(string reason)
    {
        // The reference: an organization id nobody has.
        var unknownOrganization = Guid.NewGuid();
        var unknown = await GetAsync($"/api/v1/app/organizations/{unknownOrganization}/members", Token());

        switch (reason)
        {
            case "unknown organization":
                _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);
                break;
            case "inactive organization":
                _organizations.Setup(r => r.GetByIdAsync(_organization.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateOrganization(id: _organization.Id, isActive: false));
                break;
            case "application not enabled (or lapsed)":
                // IsApplicationEnabledAsync answers false for no row, an inactive row and an expired one.
                _organizations.Setup(r => r.IsApplicationEnabledAsync(_organization.Id, _application.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);
                break;
            case "not a member":
                _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((OrganizationUser?)null);
                break;
            case "inactive membership":
                _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: _organization.Id, userId: _user.Id, isActive: false));
                break;
            default:
                _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateOrganizationUser(
                        organizationId: _organization.Id, userId: _user.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1)));
                break;
        }

        // Every member and invite permission the membership could hold: a 404 must not be a 403.
        _membershipCodes = ["org:*"];
        var notYours = await GetAsync(OrgPath("/members"), Token());

        notYours.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(notYours)).Should().Be("Organization.NotFound");
        Headers(notYours).Should().Equal(Headers(unknown));
        (await BodyWithoutTraceAsync(notYours))
            .Should().Be((await BodyWithoutTraceAsync(unknown)).Replace(unknownOrganization.ToString(), _organization.Id.ToString()),
                "the same body but for the organization id the caller sent");
    }

    // --- V3 + V4: authority from the database, never from the token ---

    [Fact]
    public async Task APlainMember_WithPlatformLookingClaimsInTheToken_Gets403OnInvitations_AndIsServedOncePromotedInTheDatabase()
    {
        // The token carries everything a platform gate would accept: an application-defined
        // permission in a platform namespace, and an org_perm wildcard for this organization.
        var token = Token(
            permissions: ["organizations:*", "*"],
            orgPermissions: [(_organization.Id, "org:*")]);
        _membershipCodes = [PermissionCodes.Org.MembersRead];

        var before = await GetAsync(OrgPath("/invitations"), token);

        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(before)).Should().Be("Organization.InsufficientPermissions");
        WwwAuthenticate(before).Should().BeEmpty("a missing membership permission is not a scope challenge");

        // Promoted in the database after the token was minted: served at once, same token.
        _membershipCodes = [PermissionCodes.Org.MembersRead, PermissionCodes.Org.MembersInvite];
        (await GetAsync(OrgPath("/invitations"), token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ANonMember_WithAWildcardInTheToken_GetsThe404()
    {
        _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);

        var response = await GetAsync(OrgPath("/invitations"),
            Token(permissions: ["organizations:*", "organizations:read", "*"], orgPermissions: [(_organization.Id, "*")]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _organizations.Verify(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- V6: the two gates, and the roles field ---

    [Fact]
    public async Task Members_WithoutMembersRead_Returns403()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];

        var response = await GetAsync(OrgPath("/members"), Token());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("Organization.InsufficientPermissions");
    }

    [Fact]
    public async Task Invitations_WithMembersReadAlone_Returns403()
    {
        _membershipCodes = [PermissionCodes.Org.MembersRead];

        var response = await GetAsync(OrgPath("/invitations"), Token());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("Organization.InsufficientPermissions");
    }

    [Fact]
    public async Task TheOrganization_NeedsMembershipAlone()
    {
        _membershipCodes = [];

        (await GetAsync(OrgPath(), Token())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Members_WithoutPermissionsRead_HaveNoApplicationRolesField()
    {
        _membershipCodes = [PermissionCodes.Org.MembersRead];

        var body = await JsonAsync(await GetAsync(OrgPath("/members"), Token()));

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Select(item => item.TryGetProperty("applicationRoles", out _)).Should().OnlyContain(present => !present);
        _organizations.Verify(r => r.GetMemberApplicationRolesAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Members_WithPermissionsRead_CarryThisApplicationsRoles_EmptyForAMemberWithNone()
    {
        _membershipCodes = [PermissionCodes.Org.MembersRead, PermissionCodes.Org.PermissionsRead];

        var response = await GetAsync(OrgPath("/members?pageNumber=1&pageSize=2"), Token());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        Members(body).Should().BeEquivalentTo("items", "totalCount", "pageNumber", "pageSize");
        body.GetProperty("totalCount").GetInt32().Should().Be(2);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var mia = items[0];
        Members(mia).Should().BeEquivalentTo("userId", "name", "email", "pictureUrl", "membershipRole", "joinedAt", "applicationRoles");
        mia.GetProperty("name").GetString().Should().Be("Mia Member");
        mia.GetProperty("membershipRole").GetString().Should().Be("org-member");
        mia.GetProperty("applicationRoles").EnumerateArray().Select(e => e.GetString()).Should().Equal("agent");
        mia.GetProperty("pictureUrl").GetString().Should().Be("https://auth.example.com/uploads/images/avatars/mia.png");

        // No name and no picture: both absent, never empty strings.
        Members(items[1]).Should().BeEquivalentTo("userId", "email", "membershipRole", "joinedAt", "applicationRoles");
        items[1].GetProperty("applicationRoles").GetArrayLength().Should().Be(0);

        // The roles are this application's, read for the whole page in one call.
        _organizations.Verify(r => r.GetMemberApplicationRolesAsync(
            _organization.Id, _application.Id,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_user.Id)),
            Now, It.IsAny<CancellationToken>()), Times.Once);
        _organizations.Verify(r => r.GetActiveMembersPagedAsync(_organization.Id, Now, 1, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- V5 + V7: the invitations the application may see, and their statuses ---

    private OrganizationInvitation Invitation(InvitationStatus stored, DateTime expiresAt, Guid? applicationId = null) =>
        new(Guid.NewGuid(), _organization.Id, "invitee@example.com", _memberRoleId, "hash",
            stored, expiresAt, Guid.NewGuid(), null, null, Now.AddDays(-2),
            InvitationApplicationRole.From(applicationId ?? _application.Id, Guid.NewGuid()), null);

    private void InvitationsReturn(params OrganizationInvitation[] invitations) =>
        _organizations
            .Setup(r => r.GetInvitationsPagedAsync(_organization.Id, It.IsAny<InvitationListFilter>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((invitations, invitations.Length));

    [Fact]
    public async Task Invitations_AskForThisApplicationsOnly_PendingAndExpiredByDefault_NewestFirst()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];

        (await GetAsync(OrgPath("/invitations"), Token())).StatusCode.Should().Be(HttpStatusCode.OK);

        _organizations.Verify(r => r.GetInvitationsPagedAsync(
            _organization.Id,
            It.Is<InvitationListFilter>(filter => filter.ApplicationId == _application.Id
                && filter.Statuses.SequenceEqual(new[] { InvitationStatus.Pending, InvitationStatus.Expired })),
            Now, 1, 20, null, SortDirection.Asc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invitations_APendingRowPastItsExpiry_ReadsExpired_AndCarriesNoTokenOrInviter()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];
        InvitationsReturn(Invitation(InvitationStatus.Pending, Now.AddMinutes(-1)), Invitation(InvitationStatus.Pending, Now.AddDays(1)));

        var body = await JsonAsync(await GetAsync(OrgPath("/invitations"), Token()));

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("status").GetString()).Should().Equal("Expired", "Pending");
        Members(items[0]).Should().BeEquivalentTo(
            "id", "email", "membershipRole", "applicationRole", "status", "createdAt", "expiresAt");
        items[0].GetProperty("membershipRole").GetString().Should().Be("org-member");
        items[0].GetProperty("applicationRole").GetString().Should().Be("agent");
    }

    [Fact]
    public async Task Invitations_AnotherApplicationsRow_NeverReachesTheAnswer()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];
        InvitationsReturn(Invitation(InvitationStatus.Pending, Now.AddDays(1), applicationId: Guid.NewGuid()));

        var body = await JsonAsync(await GetAsync(OrgPath("/invitations"), Token()));

        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("?status=Accepted&status=Declined")]
    [InlineData("?status=Cancelled")]
    [InlineData("?status=Pending&pageNumber=100000&pageSize=100")]
    public async Task Invitations_ValidQueries_Return200(string query)
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];

        (await GetAsync(OrgPath("/invitations" + query), Token())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invitations_RepeatedStatuses_ReachTheFilterAsNamed()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];

        await GetAsync(OrgPath("/invitations?status=Accepted&status=Declined"), Token());

        _organizations.Verify(r => r.GetInvitationsPagedAsync(
            _organization.Id,
            It.Is<InvitationListFilter>(filter => filter.Statuses.SequenceEqual(new[] { InvitationStatus.Accepted, InvitationStatus.Declined })),
            It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<SortDirection>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("/invitations?status=Accepted,Declined", TransportErrorCodes.BadRequest)]
    [InlineData("/invitations?status=accepted", TransportErrorCodes.BadRequest)]
    [InlineData("/invitations?status=2", TransportErrorCodes.BadRequest)]
    [InlineData("/invitations?status=", TransportErrorCodes.BadRequest)]
    [InlineData("/invitations?status=Unknown", TransportErrorCodes.BadRequest)]
    [InlineData("/invitations?pageSize=0", "Paging.PageSizeOutOfRange")]
    [InlineData("/invitations?pageSize=101", "Paging.PageSizeOutOfRange")]
    [InlineData("/invitations?pageNumber=0", "Paging.PageNumberOutOfRange")]
    [InlineData("/invitations?pageNumber=2147483647", "Paging.PageNumberOutOfRange")]
    [InlineData("/members?pageSize=0", "Paging.PageSizeOutOfRange")]
    [InlineData("/members?pageSize=101", "Paging.PageSizeOutOfRange")]
    [InlineData("/members?pageNumber=2147483647", "Paging.PageNumberOutOfRange")]
    [InlineData("/members?pageNumber=100001", "Paging.PageNumberOutOfRange")]
    [InlineData("/members?pageSize=abc", "Paging.PageSizeOutOfRange")]
    [InlineData("/members?pageNumber=", "Paging.PageNumberOutOfRange")]
    [InlineData("/members?pageNumber=-1", "Paging.PageNumberOutOfRange")]
    public async Task BadQueries_Return400_WithTheirCode(string suffix, string code)
    {
        _membershipCodes = [PermissionCodes.Org.MembersRead, PermissionCodes.Org.MembersInvite];

        var response = await GetAsync(OrgPath(suffix), Token());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(code);
        (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue();
    }

    // --- F2 (REVIEW 92): "who are you" is answered before "what did you ask" ---

    [Theory]
    [InlineData("/members?pageSize=0")]
    [InlineData("/members?pageNumber=abc")]
    [InlineData("/invitations?status=Accepted,Declined&pageSize=101")]
    public async Task ACallerThatCannotBeServed_WithABadQuery_Gets401IdenticalToARevokedToken(string suffix)
    {
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var revokedToken = Token();
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revokedToken)!)).Returns(true);

        var unservable = await GetAsync(OrgPath(suffix), Token());
        var revoked = await GetAsync(OrgPath(suffix), revokedToken);

        unservable.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "who is asking is answered before what they asked");
        (await CodeAsync(unservable)).Should().Be(ChallengeReasonCodes.TokenRevoked);
        Headers(unservable).Should().Equal(Headers(revoked));
        (await BodyWithoutTraceAsync(unservable)).Should().Be(await BodyWithoutTraceAsync(revoked));
    }

    [Theory]
    [InlineData("/invitations?status=Accepted,Declined")]
    [InlineData("/members?pageSize=0")]
    public async Task ATokenWithoutTheScope_WithABadQuery_Gets403InsufficientScope(string suffix)
    {
        var response = await GetAsync(OrgPath(suffix), Token(scope: "openid profile email"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Forbidden);
        WwwAuthenticate(response).Should().Be("Bearer error=\"insufficient_scope\", scope=\"organizations.read\"");
    }

    [Fact]
    public async Task ANonMember_WithABadQuery_GetsThe404()
    {
        _organizations.Setup(r => r.GetMembershipAsync(_organization.Id, _user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);

        var response = await GetAsync(OrgPath("/invitations?status=nope&pageSize=0"), Token());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AValidCaller_WithABadStatus_Gets400_NoStore_AndNothingIsRead()
    {
        _membershipCodes = [PermissionCodes.Org.MembersInvite];

        var response = await GetAsync(OrgPath("/invitations?status=Accepted,Declined"), Token());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.BadRequest);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        _organizations.Verify(r => r.GetInvitationsPagedAsync(
            It.IsAny<Guid>(), It.IsAny<InvitationListFilter>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<string, string?> EveryKindOfAnswer => new()
    {
        { "200", "" },
        { "400", "/members?pageSize=0" },
        { "401 no token", null },
        { "401 revoked", "revoked" },
        { "401 unservable", "unservable" },
        { "403 scope", "noscope" },
        { "403 permission", "/invitations" },
        { "404", "notfound" },
    };

    [Fact]
    public void EveryKindOfAnswer_IsNotEmpty() => EveryKindOfAnswer.Count<object[]>().Should().Be(8);

    [Theory]
    [MemberData(nameof(EveryKindOfAnswer))]
    public async Task EveryAnswer_CarriesNoStore(string kind, string? how)
    {
        _membershipCodes = [PermissionCodes.Org.MembersRead];
        HttpResponseMessage response;
        switch (how)
        {
            case null:
                response = await GetAsync(OrgPath(), token: null);
                break;
            case "revoked":
                var revoked = Token();
                _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revoked)!)).Returns(true);
                response = await GetAsync(OrgPath(), revoked);
                break;
            case "unservable":
                _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
                response = await GetAsync(OrgPath(), Token());
                break;
            case "noscope":
                response = await GetAsync(OrgPath(), Token(scope: "openid"));
                break;
            case "notfound":
                response = await GetAsync($"/api/v1/app/organizations/{Guid.NewGuid()}", Token());
                break;
            default:
                response = await GetAsync(OrgPath(how), Token());
                break;
        }

        ((int)response.StatusCode).ToString().Should().Be(kind[..3]);
        (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("{0} is an answer of the directory too", kind);
    }

    // --- V12: images an application can use, or nothing ---

    [Theory]
    [InlineData("/uploads/images/logos/org.png")]
    [InlineData("ftp://files.example.com/org.png")]
    [InlineData("https://user:secret@cdn.example.com/org.png")]
    [InlineData(null)]
    public async Task ALogoThatIsNotAnAbsoluteHttpUrlWithoutCredentials_IsAbsent(string? composed)
    {
        _images.Setup(c => c.Compose("logos/org.png")).Returns(composed);

        var body = await JsonAsync(await GetAsync(OrgPath(), Token()));

        Members(body).Should().BeEquivalentTo("id", "name");
    }

    [Fact]
    public async Task ARelativePicture_IsAbsent()
    {
        _images.Setup(c => c.Compose("avatars/mia.png")).Returns("/uploads/images/avatars/mia.png");

        var body = await JsonAsync(await GetAsync(OrgPath("/members"), Token()));

        body.GetProperty("items")[0].TryGetProperty("pictureUrl", out _).Should().BeFalse();
    }

    private sealed class TheseControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(ApplicationOrganizationsController).GetTypeInfo());
        }
    }
}
