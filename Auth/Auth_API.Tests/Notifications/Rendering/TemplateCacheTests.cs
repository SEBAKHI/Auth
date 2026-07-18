using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;
using Auth.Infrastructure.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// Unit tests for the send-path cache: loader short-circuiting, negative
/// caching, scope isolation, and direct eviction.
/// </summary>
public class TemplateCacheTests
{
    private readonly TemplateCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    private static NotificationTemplateRenderSource Source() => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, null, "en",
        [new NotificationTranslationRenderSource("en", "S", "<p>B</p>", null)]);

    [Fact]
    public async Task GetTemplateAsync_SecondCall_DoesNotInvokeLoader()
    {
        var loads = 0;
        Task<NotificationTemplateRenderSource?> Loader()
        {
            loads++;
            return Task.FromResult<NotificationTemplateRenderSource?>(Source());
        }

        await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, Loader);
        var second = await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, Loader);

        loads.Should().Be(1);
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTemplateAsync_NullResult_IsCachedNegatively()
    {
        var loads = 0;
        Task<NotificationTemplateRenderSource?> Loader()
        {
            loads++;
            return Task.FromResult<NotificationTemplateRenderSource?>(null);
        }

        var first = await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, Loader);
        var second = await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, Loader);

        loads.Should().Be(1);
        first.Should().BeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task GetTemplateAsync_DifferentAppScopes_AreIsolated()
    {
        var appId = Guid.NewGuid();
        var appSource = Source();
        var globalSource = Source();

        var fromApp = await _cache.GetTemplateAsync(
            "password-reset", NotificationChannelType.Email, appId,
            () => Task.FromResult<NotificationTemplateRenderSource?>(appSource));
        var fromGlobal = await _cache.GetTemplateAsync(
            "password-reset", NotificationChannelType.Email, null,
            () => Task.FromResult<NotificationTemplateRenderSource?>(globalSource));

        fromApp!.TemplateId.Should().Be(appSource.TemplateId);
        fromGlobal!.TemplateId.Should().Be(globalSource.TemplateId);
    }

    [Fact]
    public async Task InvalidateTemplate_EvictsOnlyThatScope()
    {
        var appId = Guid.NewGuid();
        var loadsGlobal = 0;
        var loadsApp = 0;

        Task<NotificationTemplateRenderSource?> GlobalLoader()
        {
            loadsGlobal++;
            return Task.FromResult<NotificationTemplateRenderSource?>(Source());
        }

        Task<NotificationTemplateRenderSource?> AppLoader()
        {
            loadsApp++;
            return Task.FromResult<NotificationTemplateRenderSource?>(Source());
        }

        await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, GlobalLoader);
        await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, appId, AppLoader);

        _cache.InvalidateTemplate("password-reset", NotificationChannelType.Email, null);

        await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, null, GlobalLoader);
        await _cache.GetTemplateAsync("password-reset", NotificationChannelType.Email, appId, AppLoader);

        loadsGlobal.Should().Be(2);
        loadsApp.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateLayout_EvictsLayoutScope()
    {
        var loads = 0;
        Task<NotificationLayoutRenderSource?> Loader()
        {
            loads++;
            return Task.FromResult<NotificationLayoutRenderSource?>(
                new NotificationLayoutRenderSource(Guid.NewGuid(), null, "<html></html>", "{}"));
        }

        await _cache.GetLayoutAsync(NotificationChannelType.Email, null, Loader);
        _cache.InvalidateLayout(NotificationChannelType.Email, null);
        await _cache.GetLayoutAsync(NotificationChannelType.Email, null, Loader);

        loads.Should().Be(2);
    }
}
