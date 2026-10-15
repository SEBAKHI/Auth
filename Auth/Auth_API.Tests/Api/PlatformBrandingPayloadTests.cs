using System.Net;
using System.Reflection;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPlatformBranding;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.Administration.Controllers;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.Api;

/// <summary>
/// OI-165: <c>GET /Platform/branding</c> is anonymous and tells the sign-in page
/// whether to show "Stay signed in". It publishes that one boolean, never the
/// administrator's mode (the page acts on nothing else), and stays uncached, so a
/// console change reaches the next visitor; the server applies the setting to
/// every sign-in whatever the page shows. Read off the real controller's
/// response, as a browser receives it.
/// </summary>
public class PlatformBrandingPayloadTests
{
    private static readonly string[] PublishedFields =
        ["platformName", "logoUrl", "logoUrlDark", "faviconUrl", "theme", "staySignedInOffered"];

    private static async Task<HttpResponseMessage> GetBrandingAsync(StaySignedInPolicy mode)
    {
        var repository = new Mock<IPlatformSettingsRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);
        var handler = new GetPlatformBrandingQueryHandler(
            repository.Object, new Mock<IImageUrlComposer>().Object,
            TestHelpers.CreateOptions(new IdentityProviderSettings { StaySignedInMode = mode.ToString() }));
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPlatformBrandingQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<PlatformBrandingDto>> query, CancellationToken cancellationToken) =>
                handler.Handle((GetPlatformBrandingQuery)query, cancellationToken));

        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(sender.Object);
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new PlatformControllerOnly());
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
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();

        // No credentials of any kind: the sign-in page asks before anyone signs in.
        var response = await host.GetTestClient().GetAsync("/api/v1/Platform/branding");
        await response.Content.LoadIntoBufferAsync();
        await host.StopAsync();
        return response;
    }

    [Theory]
    [InlineData(StaySignedInPolicy.PersonChooses, true)]
    [InlineData(StaySignedInPolicy.AlwaysBrowserSession, false)]
    [InlineData(StaySignedInPolicy.AlwaysStaySignedIn, false)]
    public async Task GetBranding_Anonymous_SaysWhetherTheBoxIsOffered_AndNothingMore(StaySignedInPolicy mode, bool offered)
    {
        var response = await GetBrandingAsync(mode);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("staySignedInOffered").GetBoolean().Should().Be(offered);
        body.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(PublishedFields,
            "the mode itself is never published");
        body.GetProperty("platformName").GetString().Should().Be(PlatformSettings.DefaultPlatformName);
        response.Headers.CacheControl.Should().BeNull("a console change must reach the next visitor");
    }

    [Fact]
    public void PlatformBrandingDto_PublishesTheBooleanOnly()
    {
        typeof(PlatformBrandingDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().NotContain(property => property.PropertyType == typeof(StaySignedInPolicy),
                "the page acts only on whether the box is shown");
        typeof(PlatformBrandingDto).GetProperty(nameof(PlatformBrandingDto.StaySignedInOffered))!
            .PropertyType.Should().Be<bool>();
    }

    private sealed class PlatformControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(PlatformController).GetTypeInfo());
        }
    }
}
