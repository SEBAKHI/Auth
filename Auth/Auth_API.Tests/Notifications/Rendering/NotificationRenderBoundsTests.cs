using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.PreviewNotificationLayout;
using Auth.Application.Features.Notifications.PreviewNotificationTemplate;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// The notification cap (100,000 bytes, so Gmail never clips an email) and the render budget
/// apply at every entry that renders a notification: the send path, and the two previews any
/// <c>notification-templates:read</c> holder can call.
/// </summary>
public class NotificationRenderBoundsTests
{
    private const long AllocationCeiling = 16 * 1024 * 1024;
    private const string Loop = "{% for i in (1..2400) %}{{ big | raw }}{% endfor %}";
    private const string Layout = "<html dir=\"{{ dir }}\"><body>{{ content | raw }}{{ strings.footer | raw }}</body></html>";

    private readonly Mock<INotificationTemplateRepository> _templates = new();
    private readonly Mock<INotificationLayoutRepository> _layouts = new();
    private readonly Mock<INotificationTypeRepository> _types = new();
    private readonly Guid _typeId = Guid.NewGuid();
    private readonly NotificationRenderingService _service;

    public NotificationRenderBoundsTests()
    {
        PublishLayout(Layout);
        _types.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationType(
                _typeId, "password-reset", "Password Reset", null, true, "[]",
                JsonSerializer.Serialize(new { big = new string('x', 60_000) }),
                true, DateTime.UtcNow, Guid.NewGuid(), null, null));

        _service = new NotificationRenderingService(
            new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
            _templates.Object,
            _layouts.Object,
            new Mock<IUserRepository>().Object,
            new Mock<IApplicationRepository>().Object,
            new Mock<IPlatformSettingsRepository>().Object,
            new FluidTemplateRenderer(TimeProvider.System),
            new Mock<IImageUrlComposer>().Object,
            new Mock<IImageStorageService>().Object,
            TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" }),
            new Mock<ILogger<NotificationRenderingService>>().Object);
    }

    // --- the send path ---

    [Fact]
    public async Task Send_OfAPublishedTemplateThatLoops_IsRefusedAtTheCap()
    {
        PublishTemplate("<p>{{ UserName }}</p>" + Loop);

        var (result, allocated) = await Measure(() => _service.RenderAsync(Request(), CancellationToken.None));

        result.FirstError.Code.Should().Be(TemplateErrors.OutputTooLargeCode);
        allocated.Should().BeLessThan(AllocationCeiling, "this body allocated 577 MB unbounded");
    }

    [Fact]
    public async Task Send_ThroughALayoutThatLoops_IsRefused_SinceTheLayoutOutputIsTheEmail()
    {
        PublishTemplate("<p>{{ UserName }}</p>{{ big }}");
        PublishLayout("<html>{% for i in (1..2400) %}{{ content | raw }}{% endfor %}</html>");

        var (result, allocated) = await Measure(() => _service.RenderAsync(Request(), CancellationToken.None));

        result.FirstError.Code.Should().Be(TemplateErrors.OutputTooLargeCode);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Fact]
    public async Task Send_OfAnEmailJustUnderTheCap_IsDelivered()
    {
        var body = new string('x', NotificationLimits.MaxRenderedBytes - 200);
        PublishTemplate(body);

        var result = await _service.RenderAsync(Request(), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Description : string.Empty);
        System.Text.Encoding.UTF8.GetByteCount(result.Value.BodyHtml).Should().BeLessThanOrEqualTo(NotificationLimits.MaxRenderedBytes);
    }

    // --- the previews any read-only holder can call ---

    [Fact]
    public async Task PreviewTemplate_OfALoop_IsRefusedAtTheCap()
    {
        var handler = new PreviewNotificationTemplateCommandHandler(_types.Object, _service);

        var (result, allocated) = await Measure(() => handler.Handle(
            new PreviewNotificationTemplateCommand(_typeId, "en", "Subject", Loop), CancellationToken.None));

        result.FirstError.Code.Should().Be(TemplateErrors.OutputTooLargeCode);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Fact]
    public async Task PreviewTemplate_WithASampleValueLongerThanTheCap_IsRefusedBeforeItIsRendered()
    {
        var handler = new PreviewNotificationTemplateCommandHandler(_types.Object, _service);
        var overrides = JsonSerializer.Serialize(new { huge = new string('a', 1_000_000) });

        var result = await handler.Handle(
            new PreviewNotificationTemplateCommand(
                _typeId, "en", "Subject", "{% if huge contains huge %}x{% endif %}", SampleOverridesJson: overrides),
            CancellationToken.None);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
    }

    [Fact]
    public async Task PreviewLayout_OfALoop_IsRefusedAtTheCap()
    {
        var handler = new PreviewNotificationLayoutCommandHandler(_service);

        var (result, allocated) = await Measure(() => handler.Handle(
            new PreviewNotificationLayoutCommand("{% for i in (1..30000) %}{{ content | raw }}{% endfor %}", "{}", "en"),
            CancellationToken.None));

        result.FirstError.Code.Should().Be(TemplateErrors.OutputTooLargeCode);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Fact]
    public async Task PreviewLayout_WithManyStringsEachNearTheCap_IsRefusedByTheBudgetTheyShare()
    {
        // Each string renders 96,000 characters within the step limit; 2,000 of them held at
        // once were 384 MB.
        var oneString = "{% for i in (1..2400) %}0123456789012345678901234567890123456789{% endfor %}";
        var strings = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = Enumerable.Range(0, 2_000).ToDictionary(i => $"s{i}", _ => oneString)
        });
        var handler = new PreviewNotificationLayoutCommandHandler(_service);

        var (result, allocated) = await Measure(() => handler.Handle(
            new PreviewNotificationLayoutCommand(Layout, strings, "en"), CancellationToken.None));

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    // --- helpers ---

    private static NotificationRequest Request() => new()
    {
        TypeCode = NotificationTypeCodes.PasswordReset,
        RecipientAddress = "user@example.com",
        Variables = new Dictionary<string, object?> { ["UserName"] = "Jane", ["big"] = new string('x', 60_000) }
    };

    private void PublishTemplate(string body) =>
        _templates
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplateRenderSource(
                Guid.NewGuid(), Guid.NewGuid(), 1, null, "en",
                [new NotificationTranslationRenderSource("en", "Subject", body, null)]));

    private void PublishLayout(string content) =>
        _layouts
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(
                Guid.NewGuid(), null, content, "{\"en\": {\"footer\": \"From {{ SenderName }}\"}}", CardColors.Default));

    private static async Task<(ErrorOr<T> Result, long Allocated)> Measure<T>(Func<Task<ErrorOr<T>>> work)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await work();
        return (result, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
