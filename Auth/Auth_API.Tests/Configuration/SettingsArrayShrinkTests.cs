using Auth.Application.Configuration;
using Auth.Application.SystemSettings;
using Auth.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The half of the console's promise that <see cref="SystemSettingsApplyCoverageTests"/>
/// cannot see. That suite asserts through <c>IConfiguration</c>, where a shrunk array
/// looks correct. Consumers do not read <c>IConfiguration</c> — they read a BOUND
/// options object, and the binder gets arrays wrong twice:
/// <list type="bullet">
/// <item>it APPENDS configured entries to a non-empty property initializer, so an
/// initializer is an unremovable prefix rather than a default;</item>
/// <item>it binds the database layer's shrink tombstones as real <c>""</c> members.</item>
/// </list>
/// Both keep an entry an administrator removed in the console alive at runtime, which
/// for <c>Gateway:ExemptPaths</c> means a path stays exempt from gateway-token
/// validation after the console says it is not. These tests assert on the bound object.
/// </summary>
public class SettingsArrayShrinkTests
{
    /// <summary>Builds the real layering: file values first, database overrides on top.</summary>
    private static IConfiguration BuildLayered(
        IDictionary<string, string?> fileLayer,
        params (string SectionKey, string OverridesJson)[] rows)
    {
        var baseline = new ConfigurationBuilder().AddInMemoryCollection(fileLayer).Build();
        var lengths = DbSettingsConfigurationProvider.CaptureBaselineArrayLengths(baseline);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(fileLayer)
            .AddInMemoryCollection(DbSettingsConfigurationProvider.BuildOverrideData(rows, lengths))
            .Build();
    }

    /// <summary>Binds exactly the way Program.cs does, post-configure included.</summary>
    private static T Bind<T>(IConfiguration configuration, string sectionName, Action<T> postConfigure)
        where T : class
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<T>(configuration.GetSection(sectionName));
        services.PostConfigure(postConfigure);
        return services.BuildServiceProvider().GetRequiredService<IOptions<T>>().Value;
    }

    // Both bind through the SAME post-configure delegate Program.cs registers, so a
    // change to the production rule cannot pass here while failing in the app.
    private static GatewaySettings BindGateway(IConfiguration configuration)
        => Bind<GatewaySettings>(configuration, GatewaySettings.SectionName, SettingsArrayNormalizer.Apply);

    private static ImageStorageSettings BindImageStorage(IConfiguration configuration)
        => Bind<ImageStorageSettings>(
            configuration, ImageStorageSettings.SectionName, SettingsArrayNormalizer.Apply);

    private static Dictionary<string, string?> FileArray(string key, params string[] values)
        => values.Select((v, i) => new KeyValuePair<string, string?>($"{key}:{i}", v))
            .ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void ShrinkingExemptPaths_RemovesThePathsTheConsoleRemoved_EvenWhenTheyAreCodeDefaults()
    {
        // The file layer lists six paths; three of them are also code defaults.
        var file = FileArray(
            "Gateway:ExemptPaths",
            "/.well-known/", "/health", "/ready", "/swagger", "/openapi", "/uploads/");

        // The administrator leaves exactly one path in the console.
        var configuration = BuildLayered(file, ("Gateway", """{"ExemptPaths":["/health"]}"""));

        var settings = BindGateway(configuration);

        settings.ExemptPaths.Should().Equal(
            ["/health"],
            "a path removed in the console must not survive as a code default or a file value — " +
            "it would stay exempt from gateway-token validation while the console reports it removed");
    }

    [Fact]
    public void ShrinkingAllowedContentTypes_RejectsTheTypesTheConsoleRemoved()
    {
        var file = FileArray(
            "ImageStorage:AllowedContentTypes",
            "image/png", "image/jpeg", "image/webp", "image/gif");

        var configuration = BuildLayered(
            file, ("ImageStorage", """{"AllowedContentTypes":["image/png"]}"""));

        var settings = BindImageStorage(configuration);

        settings.AllowedContentTypes.Should().Equal("image/png");
        settings.AllowedContentTypes.Should().NotContain("image/jpeg");
    }

    [Fact]
    public void ShrinkTombstones_NeverBecomeMatchableEntries()
    {
        // An empty entry is not inert: it prefix-matches every request path, and it
        // matches an upload that declares no content type.
        var configuration = BuildLayered(
            FileArray("Gateway:ExemptPaths", "/.well-known/", "/health", "/ready", "/swagger"),
            ("Gateway", """{"ExemptPaths":["/health"]}"""));

        BindGateway(configuration).ExemptPaths.Should().NotContain(string.Empty);

        var images = BuildLayered(
            FileArray("ImageStorage:AllowedContentTypes", "image/png", "image/jpeg", "image/webp"),
            ("ImageStorage", """{"AllowedContentTypes":["image/png"]}"""));

        BindImageStorage(images).AllowedContentTypes.Should().NotContain(string.Empty);
    }

    [Fact]
    public void FileLayerAlone_IsBoundExactly_WithNoCodeDefaultsUnioned()
    {
        // Regression on the same root cause without any database row: a non-empty
        // property initializer used to union itself with whatever the file declared,
        // so the running API exempted paths no configuration file listed.
        var configuration = BuildLayered(FileArray("Gateway:ExemptPaths", "/swagger", "/openapi"));

        BindGateway(configuration).ExemptPaths.Should().Equal("/swagger", "/openapi");
    }

    [Fact]
    public void NoConfigurationAtAll_FallsBackToTheCodeDefaults()
    {
        // The safety net the property initializer used to provide must survive: a
        // deployment that declares nothing still gets the health/readiness probes
        // exempt from gateway-token validation.
        var empty = new ConfigurationBuilder().Build();

        BindGateway(empty).ExemptPaths.Should().Equal(GatewaySettings.DefaultExemptPaths);
        BindImageStorage(empty).AllowedContentTypes
            .Should().Equal(ImageStorageSettings.DefaultAllowedContentTypes);
    }

    [Fact]
    public void EveryRegistryArrayField_HasABoundConsumerThatSurvivesAShrink()
    {
        // Guards the fix against a NEW array field being added to the registry with a
        // non-empty property initializer, which would silently reintroduce the bug.
        var arrayFields = SystemSettingsRegistry.Sections
            .Where(s => s.Editable)
            .SelectMany(s => s.Fields.Where(f => f.Kind == SettingKind.StringArray)
                .Select(f => s.FullKey(f)))
            .ToList();

        arrayFields.Should().BeEquivalentTo(
            ["Gateway:ExemptPaths", "Cors:AllowedOrigins", "ImageStorage:AllowedContentTypes"],
            "a new editable array field must also get a SettingsArrayNormalizer post-configure " +
            "(or a live IConfiguration read that filters empties, as Cors:AllowedOrigins does) " +
            "and a shrink test here — otherwise removing an entry in the console does nothing");
    }
}
