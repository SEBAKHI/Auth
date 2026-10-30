using System.Text.Json;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Domain.ValueObjects;
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
        var platform = await _platformSettingsRepository.GetAsync(cancellationToken);
        var model = await BuildModelAsync(request.Variables, request.ApplicationId, platform, cancellationToken);
        var composed = await ComposeAsync(
            request.Channel,
            request.ApplicationId,
            translation.LanguageCode,
            translation.Subject,
            translation.BodyHtml,
            translation.BodyText,
            model,
            platform,
            layoutContentOverride: null,
            layoutStringsJsonOverride: null,
            layoutCardColorsOverride: null,
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
            NotificationTypeCode = request.TypeCode,
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
        var platform = await _platformSettingsRepository.GetAsync(cancellationToken);
        var model = await BuildModelAsync(request.Variables, request.ApplicationId, platform, cancellationToken);

        var composed = await ComposeAsync(
            request.Channel,
            request.ApplicationId,
            language,
            request.Subject,
            request.BodyHtml,
            request.BodyText,
            model,
            platform,
            request.LayoutContentOverride,
            request.LayoutStringsJsonOverride,
            request.LayoutCardColorsOverride,
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
        PlatformSettings? platform,
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
        var platformName = platform?.PlatformName ?? _emailSettings.SenderName;

        // The template body has no card of its own, so it sees the logos on the default
        // cards; the layout gets its own colours' backgrounds in ComposeAsync.
        model["Platform"] = await BuildPlatformModelAsync(platform, CardColors.Default, cancellationToken);

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

    /// <summary>
    /// The <c>Platform</c> object, with the email logos built on the given card colours.
    /// </summary>
    private async Task<Dictionary<string, object?>> BuildPlatformModelAsync(
        PlatformSettings? platform,
        CardColors cardColors,
        CancellationToken cancellationToken)
    {
        // Null (never "") when no logo is configured: Liquid treats an empty
        // string as truthy, so the layout's {% if Platform.LogoUrl %} needs nil
        // to fall back to the text wordmark.
        var platformLogoUrl = _imageUrlComposer.Compose(platform?.LogoUrl);

        // Email cannot use the stored WebP logo — Gmail transcodes WebP to JPEG (flattening a
        // transparent mark onto black) and Outlook for Windows cannot decode it at all. The
        // layout therefore points at pre-built opaque PNG renditions instead, each built on the
        // card colour the layout draws behind it. These are looked up, never built, here: with
        // the outbox enabled this runs inside the HTTP request that triggers the mail. A missing
        // rendition stays null so the layout falls back to the text wordmark rather than
        // re-emitting the unsafe source or a background built for another card.
        var emailLogo = await _imageStorage.GetEmailLogoRenditionAsync(
            platform?.LogoUrl, EmailLogoVariant.Light, cardColors.Light, cancellationToken);
        var emailLogoDark = await _imageStorage.GetEmailLogoRenditionAsync(
            platform?.LogoUrlDark, EmailLogoVariant.Dark, cardColors.Dark, cancellationToken);

        // An externally hosted absolute URL has no rendition and cannot get one; pass it
        // through so that escape hatch keeps working.
        var emailLogoUrl = emailLogo is null
            ? (IsAbsoluteUrl(platform?.LogoUrl) ? platformLogoUrl : null)
            : _imageUrlComposer.Compose(emailLogo.Key);
        var emailLogoDarkUrl = emailLogoDark is null
            ? (IsAbsoluteUrl(platform?.LogoUrlDark) ? _imageUrlComposer.Compose(platform?.LogoUrlDark) : null)
            : _imageUrlComposer.Compose(emailLogoDark.Key);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Name"] = platform?.PlatformName ?? _emailSettings.SenderName,
            ["LogoUrl"] = string.IsNullOrWhiteSpace(platformLogoUrl) ? null : platformLogoUrl,
            ["EmailLogoUrl"] = string.IsNullOrWhiteSpace(emailLogoUrl) ? null : emailLogoUrl,
            ["EmailLogoDarkUrl"] = string.IsNullOrWhiteSpace(emailLogoDarkUrl) ? null : emailLogoDarkUrl,
            // Outlook's Word engine ignores height:auto, so the layout must state both.
            ["EmailLogoWidth"] = emailLogo?.Width,
            ["EmailLogoHeight"] = emailLogo?.Height,
            ["EmailLogoDarkWidth"] = emailLogoDark?.Width,
            ["EmailLogoDarkHeight"] = emailLogoDark?.Height
        };
    }

    private async Task<ErrorOr<(string Subject, string BodyHtml, string BodyText)>> ComposeAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        string language,
        string subjectSource,
        string bodyHtmlSource,
        string? bodyTextSource,
        Dictionary<string, object?> model,
        PlatformSettings? platform,
        string? layoutContentOverride,
        string? layoutStringsJsonOverride,
        CardColors? layoutCardColorsOverride,
        bool failOnUnknownVariables,
        CancellationToken cancellationToken)
    {
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        // One session for the whole notification: its parts (subject, body, chrome strings,
        // layout, text) share one budget, and each one's output stops at the notification cap.
        var session = _renderer.BeginSession(TemplateRenderLimits.Notification);

        // Subject: no HTML context, variables emitted raw.
        var subjectResult = RenderPart(session, subjectSource, model, language, encodeHtml: false, failOnUnknownVariables, unknown);
        if (subjectResult.IsError)
        {
            return subjectResult.Errors;
        }

        // HTML body: every variable HTML-encoded.
        var bodyResult = RenderPart(session, bodyHtmlSource, model, language, encodeHtml: true, failOnUnknownVariables, unknown);
        if (bodyResult.IsError)
        {
            return bodyResult.Errors;
        }

        // Layout: explicit override (layout draft preview) → app-specific → global.
        string layoutContent;
        string layoutStringsJson;
        CardColors cardColors;
        if (layoutContentOverride is not null)
        {
            layoutContent = layoutContentOverride;
            layoutStringsJson = layoutStringsJsonOverride ?? "{}";
            cardColors = layoutCardColorsOverride ?? CardColors.Default;
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
            cardColors = layout.CardColors;
        }

        var stringsResult = RenderChromeStrings(session, layoutStringsJson, language, model);
        if (stringsResult.IsError)
        {
            return stringsResult.Errors;
        }

        var layoutModel = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["content"] = bodyResult.Value,
            ["dir"] = Languages.GetDirection(language),
            ["lang"] = language,
            ["strings"] = stringsResult.Value,
            // The layout's own card colours, so it can paint its card from the very values
            // the logo backgrounds are built in: {{ Layout.CardColorDark }}.
            ["Layout"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CardColorLight"] = cardColors.Light.Value,
                ["CardColorDark"] = cardColors.Dark.Value
            }
        };

        // The logo URLs are chosen only now that the layout is known: a layout on another
        // card needs the backgrounds built for that card.
        if (cardColors != CardColors.Default)
        {
            layoutModel["Platform"] = await BuildPlatformModelAsync(platform, cardColors, cancellationToken);
        }

        var htmlResult = RenderPart(session, layoutContent, layoutModel, language, encodeHtml: true, failOnUnknownVariables: false, unknown);
        if (htmlResult.IsError)
        {
            return htmlResult.Errors;
        }

        // Plain-text alternative: stored template when present, derived otherwise.
        string bodyText;
        if (!string.IsNullOrWhiteSpace(bodyTextSource))
        {
            var textResult = RenderPart(session, bodyTextSource, model, language, encodeHtml: false, failOnUnknownVariables, unknown);
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

    private static ErrorOr<string> RenderPart(
        ITemplateRenderSession session,
        string source,
        IReadOnlyDictionary<string, object?> model,
        string language,
        bool encodeHtml,
        bool failOnUnknownVariables,
        HashSet<string> unknownAccumulator)
    {
        if (!failOnUnknownVariables)
        {
            return session.Render(source, model, language, encodeHtml);
        }

        var result = session.RenderTracking(source, model, language, encodeHtml, out var unresolved);
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
    private static ErrorOr<Dictionary<string, object?>> RenderChromeStrings(
        ITemplateRenderSession session,
        string stringsJson,
        string language,
        IReadOnlyDictionary<string, object?> model)
    {
        Dictionary<string, string> raw;
        try
        {
            raw = LocalizedStrings.Pick(stringsJson, language);
        }
        catch (JsonException ex)
        {
            return NotificationErrors.RenderFailed($"Invalid layout strings JSON: {ex.Message}");
        }

        var rendered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, template) in raw)
        {
            var result = session.Render(template, model, language, encodeHtml: true);
            if (result.IsError)
            {
                return result.Errors;
            }

            rendered[key] = result.Value;
        }

        return rendered;
    }

    #endregion
}
