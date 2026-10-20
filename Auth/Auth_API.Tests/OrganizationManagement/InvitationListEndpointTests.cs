using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Auth_API.Tests.ErrorContract;
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
/// OI-193 at the HTTP edge: the real <see cref="OrganizationsController"/> on a test host with
/// the API's error contract and JSON options, and a sender that records the query. Proves what
/// a handler test cannot: how <c>status</c>, <c>pageNumber</c> and <c>pageSize</c> bind from
/// the query string, that an unknown status is refused the way every bad query value is, and
/// that the body is the paged object.
/// </summary>
public sealed class InvitationListEndpointTests : IAsyncLifetime
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
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
            .ReturnsAsync((ErrorOr<PagedOrganizationInvitationsDto>)new PagedOrganizationInvitationsDto
            {
                Invitations = [new OrganizationInvitationDto { Email = "invitee@example.com", Status = "Accepted" }],
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 20,
            });

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.Configure<SecretManagementSettings>(settings => settings.EnableAdminApi = false);
                    services.AddSingleton(_sender.Object);
                    services.AddAuthentication(SignedIn.Scheme)
                        .AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.Scheme, _ => { });
                    services.AddAuthorization();
                    services.AddSingleton<IAuthorizationPolicyProvider, AnySignedInCaller>();
                    services.AddControllers()
                        .AddJsonOptions(options =>
                        {
                            // As Program.cs: camelCase, nulls omitted, enums by name.
                            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                        })
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
    public async Task WithNothing_SendsNoStatus_PageOne_OfTwenty_AndAnswersThePagedObject()
    {
        using var response = await _client.GetAsync($"/api/v1/Organizations/{OrgId}/invitations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = _sent.Should().ContainSingle().Subject;
        query.Statuses.Should().BeNullOrEmpty("no status keeps the list's historical content");
        query.PageNumber.Should().Be(1);
        query.PageSize.Should().Be(20);
        query.SortBy.Should().BeNull();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.ValueKind.Should().Be(JsonValueKind.Object, "D-193-1 (a): the list is paged");
        body.RootElement.GetProperty("invitations").GetArrayLength().Should().Be(1);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("totalPages").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task RepeatedStatus_AndPaging_AndSorting_BindIntoTheQuery()
    {
        using var response = await _client.GetAsync(
            $"/api/v1/Organizations/{OrgId}/invitations"
            + "?status=Accepted&status=Expired&pageNumber=2&pageSize=50&sortBy=acceptedAt&sortDirection=Desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = _sent.Should().ContainSingle().Subject;
        query.Statuses.Should().Equal(InvitationStatus.Accepted, InvitationStatus.Expired);
        query.PageNumber.Should().Be(2);
        query.PageSize.Should().Be(50);
        query.SortBy.Should().Be("acceptedAt");
        query.SortDirection.Should().Be(SortDirection.Desc);
        query.PlatformScope.Should().BeFalse("only the organizations:read claim widens the scope");
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("99")]
    public async Task AnUnknownStatus_IsRefusedAsAnyBadQueryValueIs_BeforeTheHandler(string status)
    {
        var problem = await ProblemResponse.ReadAsync(_client, new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/Organizations/{OrgId}/invitations?status={Uri.EscapeDataString(status)}"));

        problem.AssertContract(HttpStatusCode.BadRequest, TransportErrorCodes.BadRequest);
        _sent.Should().BeEmpty();
    }

    #region Host

    private sealed class SignedIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "SignedIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    /// <summary>Every permission policy is "signed in": this host tests binding, not authorization.</summary>
    private sealed class AnySignedInCaller(IOptions<AuthorizationOptions> options)
        : DefaultAuthorizationPolicyProvider(options)
    {
        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(
                new AuthorizationPolicyBuilder(SignedIn.Scheme).RequireAuthenticatedUser().Build());
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
