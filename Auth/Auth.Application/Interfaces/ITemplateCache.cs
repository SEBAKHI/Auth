using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;

namespace Auth.Application.Interfaces;

/// <summary>
/// In-process cache for published template and layout content on the send path.
/// Single-instance deployment: entries are evicted directly on publish,
/// unpublish, and rollback, with an absolute TTL as a safety net.
/// </summary>
public interface ITemplateCache
{
    /// <summary>
    /// Gets the published template content for the exact scope, loading it via
    /// <paramref name="loader"/> on a miss. Caches negative results (null) so
    /// missing app-specific overrides do not query the database on every send.
    /// </summary>
    Task<NotificationTemplateRenderSource?> GetTemplateAsync(
        string typeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        Func<Task<NotificationTemplateRenderSource?>> loader);

    /// <summary>
    /// Gets the published layout content for the exact scope, loading it via
    /// <paramref name="loader"/> on a miss.
    /// </summary>
    Task<NotificationLayoutRenderSource?> GetLayoutAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        Func<Task<NotificationLayoutRenderSource?>> loader);
}

/// <summary>
/// Eviction side of the template cache, invoked by publish/unpublish/rollback
/// handlers and layout publishes.
/// </summary>
public interface ITemplateCacheInvalidator
{
    /// <summary>
    /// Evicts the cached template for a (type, channel, application) scope.
    /// </summary>
    void InvalidateTemplate(string typeCode, NotificationChannelType channel, Guid? applicationId);

    /// <summary>
    /// Evicts the cached layout for a (channel, application) scope.
    /// </summary>
    void InvalidateLayout(NotificationChannelType channel, Guid? applicationId);
}
