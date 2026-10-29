using System.Text.Json;
using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>Who a design is rendered for: the application a page names, and the platform.</summary>
/// <param name="ApplicationName">
/// The application's name; on the platform's own pages, the platform's name, as notification
/// templates see it.
/// </param>
/// <param name="PlatformName">The platform's name.</param>
public sealed record LoginDesignAudience(string ApplicationName, string PlatformName);

/// <summary>
/// Renders sign-in page designs with the sandboxed Liquid renderer, the way a notification
/// layout is rendered: variables come from a fixed model only, every variable is HTML-encoded,
/// and the per-language strings are themselves Liquid rendered against the same model.
/// </summary>
/// <remarks>
/// <para>
/// The model is <c>App.Name</c>, <c>Platform.Name</c>, <c>lang</c>, <c>dir</c> and
/// <c>strings.*</c>. A string is rendered HTML-encoded and may carry markup, so a design prints
/// it with <c>{{ strings.key | raw }}</c>, as a layout prints its chrome strings.
/// </para>
/// <para>
/// <see cref="CheckForSave"/> is the save gate: the design is rendered in every supported
/// language with the lookups tracked, so a variable outside the model, even one behind a
/// <c>{% if lang == … %}</c>, is refused before any visitor could meet the blank it would leave.
/// </para>
/// </remarks>
public sealed class LoginDesignRenderer
{
    private readonly ITemplateRenderer _renderer;
    private readonly IPlatformSettingsRepository _platformSettings;
    private readonly IApplicationRepository _applications;

    public LoginDesignRenderer(
        ITemplateRenderer renderer,
        IPlatformSettingsRepository platformSettings,
        IApplicationRepository applications)
    {
        _renderer = renderer;
        _platformSettings = platformSettings;
        _applications = applications;
    }

    /// <summary>
    /// The audience of a scope: the application's name, or the platform's on the platform's
    /// own pages. Null when the application does not exist (or was deleted).
    /// </summary>
    public async Task<LoginDesignAudience?> AudienceAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        var platform = await _platformSettings.GetAsync(cancellationToken) ?? PlatformSettings.CreateDefault();
        if (applicationId is not { } id)
        {
            return new LoginDesignAudience(platform.PlatformName, platform.PlatformName);
        }

        var application = await _applications.GetByIdAsync(id, cancellationToken);
        return application is null ? null : new LoginDesignAudience(application.Name, platform.PlatformName);
    }

    /// <summary>The audience of a page that names an application it already loaded.</summary>
    public async Task<LoginDesignAudience> AudienceOfAsync(Auth.Domain.Entities.Application application, CancellationToken cancellationToken)
    {
        var platform = await _platformSettings.GetAsync(cancellationToken) ?? PlatformSettings.CreateDefault();
        return new LoginDesignAudience(application.Name, platform.PlatformName);
    }

    /// <summary>Renders a design for one language.</summary>
    public ErrorOr<string> Render(string content, string stringsJson, LoginDesignAudience audience, string language) =>
        RenderCore(content, stringsJson, audience, language, unknown: null);

    /// <summary>
    /// The save gate: the content parses, and neither it nor its strings use a variable outside
    /// the model, in any supported language.
    /// </summary>
    public ErrorOr<Success> CheckForSave(string content, string stringsJson, LoginDesignAudience audience)
    {
        var syntax = _renderer.Validate(content);
        if (syntax.IsError)
        {
            return syntax.Errors;
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var language in Languages.Supported)
        {
            var rendered = RenderCore(content, stringsJson, audience, language, unknown);
            if (rendered.IsError)
            {
                return rendered.Errors;
            }
        }

        return unknown.Count == 0
            ? Result.Success
            : LoginDesignErrors.UnknownVariables(string.Join(", ", unknown));
    }

    private ErrorOr<string> RenderCore(
        string content,
        string stringsJson,
        LoginDesignAudience audience,
        string language,
        SortedSet<string>? unknown)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["App"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = audience.ApplicationName },
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = audience.PlatformName },
            ["lang"] = language,
            ["dir"] = Languages.GetDirection(language)
        };

        Dictionary<string, string> raw;
        try
        {
            raw = LocalizedStrings.Pick(stringsJson, language);
        }
        catch (JsonException)
        {
            // Refused when it was saved; a stored document that no longer parses is a fault.
            return LoginDesignErrors.StringsInvalidJson;
        }

        var strings = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, template) in raw)
        {
            var text = RenderPart(template, model, language, unknown);
            if (text.IsError)
            {
                return text.Errors;
            }

            strings[key] = text.Value;
        }

        model["strings"] = strings;
        return RenderPart(content, model, language, unknown);
    }

    private ErrorOr<string> RenderPart(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string language,
        SortedSet<string>? unknown)
    {
        if (unknown is null)
        {
            return _renderer.Render(source, model, language, encodeHtml: true);
        }

        var result = _renderer.RenderTracking(source, model, language, encodeHtml: true, out var missing);
        unknown.UnionWith(missing);
        return result;
    }
}
