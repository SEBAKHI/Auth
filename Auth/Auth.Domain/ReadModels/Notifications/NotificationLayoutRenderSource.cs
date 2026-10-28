using Auth.Domain.ValueObjects;

namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Lean read model for the send path: the published content of one layout scope, and the
/// card colours its email logo backgrounds are built in.
/// </summary>
public sealed record NotificationLayoutRenderSource(
    Guid LayoutId,
    Guid? ApplicationId,
    string Content,
    string StringsJson,
    CardColors CardColors);
