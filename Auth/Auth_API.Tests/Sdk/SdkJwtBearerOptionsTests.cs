using Auth.Sdk;
using Auth.Sdk.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// SDK 2.1 (S12, TS8): how the Bearer scheme fetches the AuthSystem's signing keys, read off the
/// options JwtBearer actually builds. The key set is fetched over HTTPS unless the AuthSystem is on
/// this machine, again at most every hour, and never answered from the last set that worked:
/// together they bound how long a key withdrawn because it leaked keeps verifying forged tokens in
/// an application.
/// </summary>
public class SdkJwtBearerOptionsTests
{
    [Theory]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://evil.example/localhost")]
    [InlineData("http://auth.example.com")]
    public void PlainHttp_ToAnotherMachine_IsRefused_WhenTheSchemeIsBuilt(string baseUrl)
    {
        using var provider = Provider(baseUrl);

        var build = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(AuthSystemConstants.BearerScheme);

        build.Should().Throw<InvalidOperationException>().WithMessage("*HTTPS*");
    }

    [Theory]
    [InlineData("http://localhost:5100")]
    [InlineData("http://127.0.0.1:5100")]
    [InlineData("http://[::1]:5100")]
    public void PlainHttp_ToThisMachine_IsAllowed(string baseUrl)
    {
        var options = BearerOptions(baseUrl);

        options.RequireHttpsMetadata.Should().BeFalse();
    }

    [Fact]
    public void TheKeySet_IsFetchedAgainEveryHour_WithNoLastKnownGoodFallback()
    {
        var options = BearerOptions("https://auth.example.com");
        var manager = options.ConfigurationManager.Should().BeOfType<ConfigurationManager<OpenIdConnectConfiguration>>().Subject;

        options.RequireHttpsMetadata.Should().BeTrue();
        manager.AutomaticRefreshInterval.Should().Be(TimeSpan.FromHours(1), "the library default is 12 hours");
        manager.UseLastKnownGoodConfiguration.Should().BeFalse(
            "the last set that worked is the one that still holds a withdrawn key");
    }

    [Fact]
    public void TheInterval_CanBeShortened_ToFiveMinutes()
    {
        var options = BearerOptions("https://auth.example.com", TimeSpan.FromMinutes(5));

        ((BaseConfigurationManager)options.ConfigurationManager!).AutomaticRefreshInterval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(720)]
    public void AnIntervalOutsideFiveMinutesToAnHour_IsRefusedAtRegistration(int minutes)
    {
        var register = () => new ServiceCollection().AddAuthSystemAuthentication(options =>
        {
            Configure(options, "https://auth.example.com");
            options.JwksRefreshInterval = TimeSpan.FromMinutes(minutes);
        });

        register.Should().Throw<InvalidOperationException>().WithMessage("*JwksRefreshInterval*");
    }

    private static JwtBearerOptions BearerOptions(string baseUrl, TimeSpan? interval = null)
    {
        var provider = Provider(baseUrl, interval);
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(AuthSystemConstants.BearerScheme);
    }

    private static ServiceProvider Provider(string baseUrl, TimeSpan? interval = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthSystemAuthentication(options =>
        {
            Configure(options, baseUrl);
            if (interval is { } value)
            {
                options.JwksRefreshInterval = value;
            }
        });
        return services.BuildServiceProvider();
    }

    private static void Configure(AuthSystemOptions options, string baseUrl)
    {
        options.BaseUrl = baseUrl;
        options.Issuer = baseUrl;
        options.Audience = "CRM";
    }
}
