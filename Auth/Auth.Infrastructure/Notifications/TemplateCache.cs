using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// IMemoryCache-backed implementation of the send-path template/layout cache.
/// Single-instance deployment: direct eviction on publish/unpublish/rollback is
/// authoritative; the absolute TTL only guards against out-of-band DB edits.
/// Null results are cached too, so missing app-specific overrides do not hit the
/// database on every send.
/// </summary>
public class TemplateCache : ITemplateCache, ITemplateCacheInvalidator
{
    private static readonly TimeSpan AbsoluteTtl = TimeSpan.FromMinutes(15);

    // Sentinel stored for negative results (IMemoryCache cannot distinguish
    // "cached null" from "miss" through TryGetValue's out value alone... it can,
    // but a sentinel keeps the intent explicit).
    private static readonly object NullSentinel = new();

    private readonly IMemoryCache _cache;

    public TemplateCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<NotificationTemplateRenderSource?> GetTemplateAsync(
        string typeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        Func<Task<NotificationTemplateRenderSource?>> loader)
    {
        var key = TemplateKey(typeCode, channel, applicationId);
        return await GetOrLoadAsync(key, loader);
    }

    /// <inheritdoc />
    public async Task<NotificationLayoutRenderSource?> GetLayoutAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        Func<Task<NotificationLayoutRenderSource?>> loader)
    {
        var key = LayoutKey(channel, applicationId);
        return await GetOrLoadAsync(key, loader);
    }

    /// <inheritdoc />
    public void InvalidateTemplate(string typeCode, NotificationChannelType channel, Guid? applicationId)
    {
        _cache.Remove(TemplateKey(typeCode, channel, applicationId));
    }

    /// <inheritdoc />
    public void InvalidateLayout(NotificationChannelType channel, Guid? applicationId)
    {
        _cache.Remove(LayoutKey(channel, applicationId));
    }

    private async Task<T?> GetOrLoadAsync<T>(string key, Func<Task<T?>> loader) where T : class
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return ReferenceEquals(cached, NullSentinel) ? null : (T?)cached;
        }

        var loaded = await loader();
        _cache.Set(key, (object?)loaded ?? NullSentinel, AbsoluteTtl);
        return loaded;
    }

    private static string TemplateKey(string typeCode, NotificationChannelType channel, Guid? applicationId) =>
        $"nt:{(byte)channel}:{typeCode.ToLowerInvariant()}:{applicationId?.ToString() ?? "global"}";

    private static string LayoutKey(NotificationChannelType channel, Guid? applicationId) =>
        $"nl:{(byte)channel}:{applicationId?.ToString() ?? "global"}";
}
