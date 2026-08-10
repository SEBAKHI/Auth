using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// Orchestrates the render pipeline: template resolution (app scope then global),
/// language selection (explicit override → recipient profile → hint → template
/// default → en → any), Liquid rendering of subject/bodies, and layout composition
/// with direction and localized chrome strings. The database is the only content
/// source — there is no code fallback by design; missing published content fails loudly.
/// </summary>
public class NotificationRenderingService : INotificationRenderer
{
    private readonly ITemplateCache _cache;
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly ITemplateRenderer _renderer;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IImageStorageService _imageStorage;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<NotificationRenderingService> _logger;

    public NotificationRenderingService(
        ITemplateCache cache,
        INotificationTemplateRepository templateRepository,
        INotificationLayoutRepository layoutRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        IPlatformSettingsRepository platformSettingsRepository,
        ITemplateRenderer renderer,
        IImageUrlComposer imageUrlComposer,
        IImageStorageService imageStorage,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<NotificationRenderingService> logger)
    {
        _cache = cache;
        _templateRepository = templateRepository;
        _layoutRepository = layoutRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _platformSettingsRepository = platformSettingsRepository;
        _renderer = renderer;
        _imageUrlComposer = imageUrlComposer;
        _imageStorage = imageStorage;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<RenderedNotification>> RenderAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve the published template: app-specific override first, global fallback second.
        var source = await ResolveTemplateAsync(request.TypeCode, request.Channel, request.ApplicationId, cancellationToken);
        if (source is null)
        {
            _logger.LogError(
                "No published notification template for type {TypeCode}, channel {Channel}, application {ApplicationId}",
                request.TypeCode, request.Channel, request.ApplicationId);
            return NotificationErrors.TemplateNotPublished(request.TypeCode);
        }

        // 2. Resolve the language chain and pick the translation.
        var language = await ResolveLanguageAsync(request, source.DefaultLanguage, cancellationToken);
        var translation = PickTranslation(source, language);
        if (translation is null)
        {
            return NotificationErrors.TemplateNotPublished(request.TypeCode);
        }

        // 3. Render content and compose with the layout.
        var model = await BuildModelAsync(request.Variables, request.ApplicationId, cancellationToken);
        var composed = await ComposeAsync(
            request.Channel,
            request.ApplicationId,
            translation.LanguageCode,
            translation.Subject,
            translation.BodyHtml,
            translation.BodyText,
            model,
            layoutContentOverride: null,
            layoutStringsJsonOverride: null,
            failOnUnknownVariables: false,
            cancellationToken);

        if (composed.IsError)
        {
            return composed.Errors;
        }

        return new RenderedNotification
        {
            Channel = request.Channel,
            RecipientAddress = request.RecipientAddress,
            RecipientName = request.RecipientName,
            LanguageCode = translation.LanguageCode,
            Subject = composed.Value.Subject,
            BodyHtml = composed.Value.BodyHtml,
            BodyText = composed.Value.BodyText,
            TemplateId = source.TemplateId,
            TemplateVersionId = source.PublishedVersionId,
            TemplateVersionNumber = source.PublishedVersionNumber
        };
    }

    /// <inheritdoc />
    public async Task<ErrorOr<RenderedNotification>> RenderContentAsync(
        NotificationContentRenderRequest request,
        CancellationToken cancellationToken)
    {
        var language = Languages.Normalize(request.LanguageCode) ?? Languages.Default;
        var model = await BuildModelAsync(request.Variables, request.ApplicationId, cancellationToken);

        var composed = await ComposeAsync(
            request.Channel,
            request.ApplicationId,
            language,
            request.Subject,
            request.BodyHtml,
            request.BodyText,
            model,
            request.LayoutContentOverride,
            request.LayoutStringsJsonOverride,
            request.FailOnUnknownVariables,
            cancellationToken);

        if (composed.IsError)
        {
            return composed.Errors;
        }

        return new RenderedNotification
        {
            Channel = request.Channel,
            RecipientAddress = string.Empty,
            LanguageCode = language,
            Subject = composed.Value.Subject,
            BodyHtml = composed.Value.BodyHtml,
            BodyText = composed.Value.BodyText
        };
    }

    #region Resolution

    private async Task<NotificationTemplateRenderSource?> ResolveTemplateAsync(
        string typeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        if (applicationId is not null)
        {
            var appScoped = await _cache.GetTemplateAsync(
                typeCode, channel, applicationId,
                () => _templateRepository.GetPublishedAsync(typeCode, channel, applicationId, cancellationToken));
            if (appScoped is not null)
            {
                return appScoped;
            }
        }

        return await _cache.GetTemplateAsync(
            typeCode, channel, null,
            () => _templateRepository.GetPublishedAsync(typeCode, channel, null, cancellationToken));
    }

    /// <summary>
    /// Language chain: explicit override → recipient profile (by user id, then by
    /// email — new users get the site language chosen at registration because it
    /// is stored as their PreferredLanguage) → request-culture hint → template default.
    /// </summary>
    private async Task<string> ResolveLanguageAsync(
        NotificationRequest request,
        string templateDefaultLanguage,
        CancellationToken cancellationToken)
    {
        if (Languages.Normalize(request.LanguageCode) is { } explicitLanguage)
        {
            return explicitLanguage;
        }

        if (request.RecipientUserId is { } userId)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (Languages.Normalize(user?.PreferredLanguage) is { } profileLanguage)
            {
                return profileLanguage;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RecipientAddress))
        {
            // External recipients (e.g. invitations) may still match an existing account.
            var user = await _userRepository.GetByEmailAsync(request.RecipientAddress, cancellationToken);
            if (Languages.Normalize(user?.PreferredLanguage) is { } profileLanguage)
            {
                return profileLanguage;
            }
        }

        if (Languages.Normalize(request.LanguageHint) is { } hintLanguage)
        {
            return hintLanguage;
        }

        return Languages.Normalize(templateDefaultLanguage) ?? Languages.Default;
    }

    /// <summary>
    /// Picks the translation: resolved language → template default → en → first
    /// available (deterministic) — a translation gap never blocks a critical send.
    /// </summary>
    private static NotificationTranslationRenderSource? PickTranslation(
        NotificationTemplateRenderSource source,
        string language)
    {
        NotificationTranslationRenderSource? Find(string lang) =>
            source.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, lang, StringComparison.OrdinalIgnoreCase));

        return Find(language)
            ?? Find(source.DefaultLanguage)
            ?? Find(Languages.Default)
            ?? source.Translations.OrderBy(t => t.LanguageCode, StringComparer.Ordinal).FirstOrDefault();
    }

    #endregion

    #region Composition

    private static bool IsAbsoluteUrl(string? value) =>
        value is not null &&
        (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private async Task<Dictionary<string, object?>> BuildModelAsync(
        IReadOnlyDictionary<string, object?> variables,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        // Globals the renderer supplies to every template and layout. Explicit
        // request values win over globals so a flow can override context.
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SenderName"] = _emailSettings.SenderName,
            ["Year"] = DateTime.UtcNow.Year
        };

        // Context objects always available to templates: {{ Platform.Name }},
        // {{ Application.Name }} / {{ Application.Code }} / {{ Application.BaseUrl }}.
        // This lets admin-authored variables reference real linked-app and platform
        // data without the calling flow having to pass it. Recipient/flow-specific
        // values still come from the caller's Variables (merged last, they win).
        var platform = await _platformSettingsRepository.GetAsync(cancellationToken);
        var platformName = platform?.PlatformName ?? _emailSettings.SenderName;
        // Null (never "") when no logo is configured: Liquid treats an empty
        // string as truthy, so the layout's {% if Platform.LogoUrl %} needs nil
        // to fall back to the text wordmark.
        var platformLogoUrl = _imageUrlComposer.Compose(platform?.LogoUrl);

        // Email cannot use the stored WebP logo — Gmail transcodes WebP to JPEG (flattening a
        // transparent mark onto black) and Outlook for Windows cannot decode it at all. The
        // layout therefore points at pre-built opaque PNG renditions instead. These are looked
        // up, never built, here: with the outbox enabled this runs inside the HTTP request that
        // triggers the mail. A missing rendition stays null so the layout falls back to the
        // text wordmark rather than re-emitting the unsafe source.
        var emailLogo = await _imageStorage.GetEmailLogoRenditionAsync(
            platform?.LogoUrl, EmailLogoVariant.Light, cancellationToken);
        var emailLogoDark = await _imageStorage.GetEmailLogoRenditionAsync(
            platform?.LogoUrlDark, EmailLogoVariant.Dark, cancellationToken);

        // An externally hosted absolute URL has no rendition and cannot get one; pass it
        // through so that escape hatch keeps working.
        var emailLogoUrl = emailLogo is null
            ? (IsAbsoluteUrl(platform?.LogoUrl) ? platformLogoUrl : null)
            : _imageUrlComposer.Compose(emailLogo.Key);
        var emailLogoDarkUrl = emailLogoDark is null
            ? (IsAbsoluteUrl(platform?.LogoUrlDark) ? _imageUrlComposer.Compose(platform?.LogoUrlDark) : null)
            : _imageUrlComposer.Compose(emailLogoDark.Key);

        model["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Name"] = platformName,
            ["LogoUrl"] = string.IsNullOrWhiteSpace(platformLogoUrl) ? null : platformLogoUrl,
            ["EmailLogoUrl"] = string.IsNullOrWhiteSpace(emailLogoUrl) ? null : emailLogoUrl,
            ["EmailLogoDarkUrl"] = string.IsNullOrWhiteSpace(emailLogoDarkUrl) ? null : emailLogoDarkUrl,
            // Outlook's Word engine ignores height:auto, so the layout must state both.
            ["EmailLogoWidth"] = emailLogo?.Width,
            ["EmailLogoHeight"] = emailLogo?.Height,
            ["EmailLogoDarkWidth"] = emailLogoDark?.Width,
            ["EmailLogoDarkHeight"] = emailLogoDark?.Height
        };

        // Application must ALWAYS be present (the variable catalog promises it):
        // global templates — and the publish gate, which validates them without an
        // application scope — fall back to platform identity instead of leaving
        // the key unresolved, which would block publishing and render blanks.
        var applicationModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Name"] = platformName,
            ["Code"] = string.Empty,
            ["BaseUrl"] = _emailSettings.FrontendBaseUrl
        };

        if (applicationId is { } appId)
        {
            var application = await _applicationRepository.GetByIdAsync(appId, cancellationToken);
            if (application is not null)
            {
                applicationModel["Name"] = application.Name;
                applicationModel["Code"] = application.Code;
                applicationModel["BaseUrl"] = application.BaseUrl;
            }
        }

        model["Application"] = applicationModel;

        foreach (var (key, value) in variables)
        {
            model[key] = value;
        }

        return model;
    }

    private async Task<ErrorOr<(string Subject, string BodyHtml, string BodyText)>> ComposeAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        string language,
        string subjectSource,
        string bodyHtmlSource,
        string? bodyTextSource,
        Dictionary<string, object?> model,
        string? layoutContentOverride,
        string? layoutStringsJsonOverride,
        bool failOnUnknownVariables,
        CancellationToken cancellationToken)
    {
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        // Subject: no HTML context, variables emitted raw.
        var subjectResult = RenderPart(subjectSource, model, language, encodeHtml: false, failOnUnknownVariables, unknown);
        if (subjectResult.IsError)
        {
            return subjectResult.Errors;
        }

        // HTML body: every variable HTML-encoded.
        var bodyResult = RenderPart(bodyHtmlSource, model, language, encodeHtml: true, failOnUnknownVariables, unknown);
        if (bodyResult.IsError)
        {
            return bodyResult.Errors;
        }

        // Layout: explicit override (layout draft preview) → app-specific → global.
        string layoutContent;
        string layoutStringsJson;
        if (layoutContentOverride is not null)
        {
            layoutContent = layoutContentOverride;
            layoutStringsJson = layoutStringsJsonOverride ?? "{}";
        }
        else
        {
            var layout = await ResolveLayoutAsync(channel, applicationId, cancellationToken);
            if (layout is null)
            {
                _logger.LogError(
                    "No published notification layout for channel {Channel}, application {ApplicationId}",
                    channel, applicationId);
                return NotificationErrors.LayoutNotPublished;
            }

            layoutContent = layout.Content;
            layoutStringsJson = layout.StringsJson;
        }

        var stringsResult = RenderChromeStrings(layoutStringsJson, language, model);
        if (stringsResult.IsError)
        {
            return stringsResult.Errors;
        }

        var layoutModel = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["content"] = bodyResult.Value,
            ["dir"] = Languages.GetDirection(language),
            ["lang"] = language,
            ["strings"] = stringsResult.Value
        };

        var htmlResult = RenderPart(layoutContent, layoutModel, language, encodeHtml: true, failOnUnknownVariables: false, unknown);
        if (htmlResult.IsError)
        {
            return htmlResult.Errors;
        }

        // Plain-text alternative: stored template when present, derived otherwise.
        string bodyText;
        if (!string.IsNullOrWhiteSpace(bodyTextSource))
        {
            var textResult = RenderPart(bodyTextSource, model, language, encodeHtml: false, failOnUnknownVariables, unknown);
            if (textResult.IsError)
            {
                return textResult.Errors;
            }

            bodyText = textResult.Value;
        }
        else
        {
            bodyText = HtmlToTextConverter.Convert(bodyResult.Value);
        }

        if (failOnUnknownVariables && unknown.Count > 0)
        {
            return NotificationErrors.UnknownVariables(
                string.Join(", ", unknown.Order(StringComparer.Ordinal)));
        }

        return (subjectResult.Value, htmlResult.Value, bodyText);
    }

    private ErrorOr<string> RenderPart(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string language,
        bool encodeHtml,
        bool failOnUnknownVariables,
        HashSet<string> unknownAccumulator)
    {
        if (!failOnUnknownVariables)
        {
            return _renderer.Render(source, model, language, encodeHtml);
        }

        var result = _renderer.RenderTracking(source, model, language, encodeHtml, out var unresolved);
        foreach (var name in unresolved)
        {
            unknownAccumulator.Add(name);
        }

        return result;
    }

    private async Task<NotificationLayoutRenderSource?> ResolveLayoutAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        if (applicationId is not null)
        {
            var appScoped = await _cache.GetLayoutAsync(
                channel, applicationId,
                () => _layoutRepository.GetPublishedAsync(channel, applicationId, cancellationToken));
            if (appScoped is not null)
            {
                return appScoped;
            }
        }

        return await _cache.GetLayoutAsync(
            channel, null,
            () => _layoutRepository.GetPublishedAsync(channel, null, cancellationToken));
    }

    /// <summary>
    /// Extracts the chrome strings for the language (with the standard fallback
    /// chain) and renders each string as a Liquid template against the model so
    /// placeholders like SenderName resolve.
    /// </summary>
    private ErrorOr<Dictionary<string, object?>> RenderChromeStrings(
        string stringsJson,
        string language,
        IReadOnlyDictionary<string, object?> model)
    {
        Dictionary<string, string> raw;
        try
        {
            using var document = JsonDocument.Parse(stringsJson);
            raw = ExtractLanguageStrings(document, language);
        }
        catch (JsonException ex)
        {
            return NotificationErrors.RenderFailed($"Invalid layout strings JSON: {ex.Message}");
        }

        var rendered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, template) in raw)
        {
            var result = _renderer.Render(template, model, language, encodeHtml: true);
            if (result.IsError)
            {
                return result.Errors;
            }

            rendered[key] = result.Value;
        }

        return rendered;
    }

    private static Dictionary<string, string> ExtractLanguageStrings(JsonDocument document, string language)
    {
        var root = document.RootElement;

        JsonElement? Pick(string lang) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(lang, out var element) &&
            element.ValueKind == JsonValueKind.Object
                ? element
                : null;

        var selected = Pick(language)
            ?? Pick(Languages.Default)
            ?? (root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => (JsonElement?)p.Value)
                    .FirstOrDefault()
                : null);

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selected is { } element)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    strings[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return strings;
    }

    #endregion
}
