namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Lean read model for the send path: the published content of one layout scope.
/// </summary>
public sealed record NotificationLayoutRenderSource(
    Guid LayoutId,
    Guid? ApplicationId,
    string Content,
    string StringsJson);
