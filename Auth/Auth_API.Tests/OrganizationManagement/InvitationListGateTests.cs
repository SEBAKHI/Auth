using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.GetOrganizationMembers;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Authorization;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Auth_Localization.Extensions;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.OrganizationManagement;

/// <summary>
/// D-91-1 (b2): an organization's invitation list is the inviters' business. The real
/// <see cref="OrganizationsController"/> runs behind the API's own permission policy and
/// <see cref="PermissionRequirementHandler"/>, with the caller's claims set per request, so
/// what is asserted is the gate the API enforces, organization-scoped claims and the
/// platform administrator's path included.
/// </summary>
public sealed class InvitationListGateTests : IAsyncLifetime
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrgId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ClaimsHeader = "X-Test-Claims";

    private readonly Mock<ISender> _sender = new();
    private readonly List<GetPendingInvitationsQuery> _sent = [];
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _sender
            .Setup(s => s.Send(It.IsAny<GetPendingInvitationsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<PagedOrganizationInvitationsDto>>, CancellationToken>(
                (query, _) => _sent.Add((GetPendingInvitationsQuery)query))
            .ReturnsAsync((ErrorOr<PagedOrganizationInvitationsDto>)new PagedOrganizationInvitationsDto { PageSize = 20 });
        _sender
            .Setup(s => s.Send(It.IsAny<GetOrganizationMembersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PagedOrganizationMembersDto>)new PagedOrganizationMembersDto { PageSize = 20 });

        // The gate's live-membership fallback (a token that predates the membership): this
        // caller is a member of nothing.
        var organizations = new Mock<IOrganizationRepository>();
        organizations
            .Setup(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.Configure<SecretManagementSettings>(settings => settings.EnableAdminApi = false);
                    services.AddSingleton(_sender.Object);
                    services.AddSingleton(organizations.Object);
                    services.AddAuthentication(FromHeader.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, FromHeader>(FromHeader.SchemeName, _ => { });
                    services.AddAuthorization();
                    // The API's own policy provider and handler (Program.cs).
                    services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
                    services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
                    services.AddControllers()
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new OrganizationsControllerOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        options.ApiVersionReader = new UrlSegmentApiVersionReader();
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
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
    }

    [Fact]
    public async Task APlainMember_WhoMayReadMembers_IsRefusedTheInvitationList()
    {
        var claims = OrgPerm(OrgId, PermissionCodes.Org.MembersRead) + ";" + OrgPerm(OrgId, PermissionCodes.Org.AppsRead);

        (await Get($"/api/v1/Organizations/{OrgId}/invitations", claims)).Should().Be(HttpStatusCode.Forbidden,
            "reading the members does not entitle a member to who was invited, who accepted and who invited them");
        (await Get($"/api/v1/Organizations/{OrgId}/members", claims)).Should().Be(HttpStatusCode.OK,
            "the same caller still reads the members list: the difference is the gate, not the token");
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("org:members:invite")]
    [InlineData("org:members:*")]
    [InlineData("org:*")]
    public async Task AnInviterInThatOrganization_ReadsTheList(string code)
    {
        (await Get($"/api/v1/Organizations/{OrgId}/invitations", OrgPerm(OrgId, code)))
            .Should().Be(HttpStatusCode.OK);
        _sent.Should().ContainSingle().Which.PlatformScope.Should().BeFalse();
    }

    [Fact]
    public async Task AnInviterInAnotherOrganization_IsRefused()
    {
        (await Get($"/api/v1/Organizations/{OrgId}/invitations", OrgPerm(OtherOrgId, PermissionCodes.Org.MembersInvite)))
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APlatformAdministrator_PassesAsBefore_AndIsWidened()
    {
        (await Get($"/api/v1/Organizations/{OrgId}/invitations", "permissions=*"))
            .Should().Be(HttpStatusCode.OK, "the platform permissions claim is checked first, as for every org: gate");
        _sent.Should().ContainSingle().Which.PlatformScope.Should().BeTrue();
    }

    [Fact]
    public async Task OrganizationsReadAlone_DoesNotPassTheGate_AsItDidNotBefore()
    {
        // Unchanged by this PR: organizations:read widens a request the gate admitted; it is
        // not itself an org: code, so a non-member holding only it was refused before (on
        // org:members:read) and is refused now.
        (await Get($"/api/v1/Organizations/{OrgId}/invitations", "permissions=" + PermissionCodes.Organizations.Read))
            .Should().Be(HttpStatusCode.Forbidden);
        (await Get($"/api/v1/Organizations/{OrgId}/members", "permissions=" + PermissionCodes.Organizations.Read))
            .Should().Be(HttpStatusCode.Forbidden);
    }

    private static string OrgPerm(Guid organizationId, string code) => $"org_perm={organizationId}:{code}";

    private async Task<HttpStatusCode> Get(string path, string claims)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ClaimsHeader, claims);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    #region Host

    /// <summary>Signs the caller in with the claims named in the request header ("type=value;…").</summary>
    private sealed class FromHeader(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FromHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(JwtClaimNames.Subject, Guid.NewGuid().ToString()) };
            foreach (var pair in Request.Headers[ClaimsHeader].ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                claims.Add(new Claim(pair[..separator], pair[(separator + 1)..]));
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }

    private sealed class OrganizationsControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(OrganizationsController).GetTypeInfo());
        }
    }

    #endregion
}
