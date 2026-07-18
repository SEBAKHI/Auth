using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// Tests for the rendering orchestrator: template resolution with app-to-global
/// fallback, the full language chain, layout composition (LTR + RTL), chrome
/// string rendering, and plain-text derivation.
/// </summary>
public class NotificationRenderingServiceTests
{
    private const string Layout =
        "<html dir=\"{{ dir }}\" lang=\"{{ lang }}\"><body>{{ content | raw }}<footer>{{ strings.footer | raw }}</footer></body></html>";

    private const string LayoutStrings =
        "{\"en\": {\"footer\": \"Automated message from {{ SenderName }}.\"}," +
        "\"ar\": {\"footer\": \"رسالة تلقائية من {{ SenderName }}.\"}}";

    private readonly Mock<INotificationTemplateRepository> _templateRepoMock = new();
    private readonly Mock<INotificationLayoutRepository> _layoutRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepoMock = new();
    private readonly Mock<IPlatformSettingsRepository> _platformRepoMock = new();
    private readonly NotificationRenderingService _service;

    public NotificationRenderingServiceTests()
    {
        _layoutRepoMock
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(Guid.NewGuid(), null, Layout, LayoutStrings));

        var settings = TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" });

        _service = new NotificationRenderingService(
            new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
            _templateRepoMock.Object,
            _layoutRepoMock.Object,
            _userRepoMock.Object,
            _applicationRepoMock.Object,
            _platformRepoMock.Object,
            new FluidTemplateRenderer(),
            settings,
            new Mock<ILogger<NotificationRenderingService>>().Object);
    }

    private static NotificationTemplateRenderSource TemplateSource(
        params (string Lang, string Subject, string Body)[] translations)
    {
        return new NotificationTemplateRenderSource(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            null,
            "en",
            translations
                .Select(t => new NotificationTranslationRenderSource(t.Lang, t.Subject, t.Body, null))
                .ToList());
    }

    private void SetupGlobalTemplate(NotificationTemplateRenderSource source)
    {
        _templateRepoMock
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
    }

    [Fact]
    public async Task RenderAsync_NoPublishedTemplate_FailsLoudlyWithoutCodeFallback()
    {
        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com"
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.TemplateNotPublished");
    }

    [Fact]
    public async Task RenderAsync_EnglishRecipient_ComposesLtrDocument()
    {
        SetupGlobalTemplate(TemplateSource(
            ("en", "Reset your password", "<p>Hello {{ UserName }}</p>")));

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                Variables = new Dictionary<string, object?> { ["UserName"] = "Jane" }
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Subject.Should().Be("Reset your password");
        result.Value.LanguageCode.Should().Be("en");
        result.Value.BodyHtml.Should().Contain("dir=\"ltr\"");
        result.Value.BodyHtml.Should().Contain("lang=\"en\"");
        result.Value.BodyHtml.Should().Contain("<p>Hello Jane</p>");
        result.Value.BodyHtml.Should().Contain("Automated message from Auth System.");
        result.Value.BodyText.Should().Contain("Hello Jane");
    }

    [Fact]
    public async Task RenderAsync_ArabicProfileLanguage_ComposesRtlDocumentFromProfile()
    {
        SetupGlobalTemplate(TemplateSource(
            ("en", "Reset your password", "<p>Hello {{ UserName }}</p>"),
            ("ar", "إعادة تعيين كلمة المرور", "<p>مرحبًا {{ UserName }}</p>")));

        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com", preferredLanguage: "ar");
        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                RecipientUserId = userId,
                Variables = new Dictionary<string, object?> { ["UserName"] = "جين" }
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("ar");
        result.Value.Subject.Should().Be("إعادة تعيين كلمة المرور");
        result.Value.BodyHtml.Should().Contain("dir=\"rtl\"");
        result.Value.BodyHtml.Should().Contain("رسالة تلقائية من Auth System.");
    }

    [Fact]
    public async Task RenderAsync_ExplicitLanguage_OverridesProfile()
    {
        SetupGlobalTemplate(TemplateSource(
            ("en", "EN subject", "<p>en</p>"),
            ("fr", "FR subject", "<p>fr</p>")));

        var userId = Guid.NewGuid();
        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, preferredLanguage: "ar"));

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                RecipientUserId = userId,
                LanguageCode = "fr"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("fr");
        result.Value.Subject.Should().Be("FR subject");
    }

    [Fact]
    public async Task RenderAsync_ExternalRecipientMatchedByEmail_UsesProfileLanguage()
    {
        SetupGlobalTemplate(TemplateSource(
            ("en", "EN subject", "<p>en</p>"),
            ("tr", "TR subject", "<p>tr</p>")));

        _userRepoMock
            .Setup(r => r.GetByEmailAsync("invitee@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(email: "invitee@example.com", preferredLanguage: "tr"));

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "invitee@example.com",
                LanguageHint = "fr"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("tr");
    }

    [Fact]
    public async Task RenderAsync_NoProfileMatch_FallsBackToLanguageHint()
    {
        SetupGlobalTemplate(TemplateSource(
            ("en", "EN subject", "<p>en</p>"),
            ("fr", "FR subject", "<p>fr</p>")));

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "external@example.com",
                LanguageHint = "fr"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("fr");
    }

    [Fact]
    public async Task RenderAsync_TranslationGap_FallsBackToDefaultLanguageContent()
    {
        // Recipient prefers zh but the template only has en: send must not fail.
        SetupGlobalTemplate(TemplateSource(("en", "EN subject", "<p>en body</p>")));

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                LanguageCode = "zh"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("en");
        result.Value.Subject.Should().Be("EN subject");
    }

    [Fact]
    public async Task RenderAsync_AppScopedTemplate_PreferredOverGlobal()
    {
        var appId = Guid.NewGuid();
        var appSource = TemplateSource(("en", "APP subject", "<p>app</p>"));
        var globalSource = TemplateSource(("en", "GLOBAL subject", "<p>global</p>"));

        _templateRepoMock
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appSource);
        SetupGlobalTemplate(globalSource);

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                ApplicationId = appId
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Subject.Should().Be("APP subject");
    }

    [Fact]
    public async Task RenderAsync_NoAppOverride_FallsBackToGlobalTemplate()
    {
        var appId = Guid.NewGuid();
        _templateRepoMock
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationTemplateRenderSource?)null);
        SetupGlobalTemplate(TemplateSource(("en", "GLOBAL subject", "<p>global</p>")));

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                ApplicationId = appId
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Subject.Should().Be("GLOBAL subject");
    }

    [Fact]
    public async Task RenderAsync_ContextVariables_ResolvePlatformAndApplication()
    {
        var appId = Guid.NewGuid();
        _templateRepoMock
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TemplateSource(
                ("en", "Reset", "<p>{{ Platform.Name }} / {{ Application.Name }} ({{ Application.Code }})</p>")));
        _layoutRepoMock
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLayoutRenderSource?)null);

        _platformRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Auth.Domain.Entities.PlatformSettings(
                Auth.Domain.Entities.PlatformSettings.SingletonId, "Acme Platform", null, null, null, null, null));
        _applicationRepoMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId, name: "Billing", code: "billing"));

        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                ApplicationId = appId
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.BodyHtml.Should().Contain("Acme Platform / Billing (billing)");
    }

    [Fact]
    public async Task RenderContentAsync_UnknownVariable_FailsWhenValidationRequested()
    {
        var result = await _service.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                LanguageCode = "en",
                Subject = "Subject {{ Typo }}",
                BodyHtml = "<p>{{ AnotherTypo }}</p>",
                Variables = new Dictionary<string, object?> { ["UserName"] = "Jane" },
                FailOnUnknownVariables = true
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.UnknownVariables");
        result.FirstError.Description.Should().Contain("Typo").And.Contain("AnotherTypo");
    }

    [Fact]
    public async Task RenderContentAsync_LayoutOverride_UsesSuppliedLayout()
    {
        var result = await _service.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                LanguageCode = "en",
                Subject = "S",
                BodyHtml = "<p>body</p>",
                LayoutContentOverride = "<custom dir=\"{{ dir }}\">{{ content | raw }}</custom>"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.BodyHtml.Should().Be("<custom dir=\"ltr\"><p>body</p></custom>");
    }
}
