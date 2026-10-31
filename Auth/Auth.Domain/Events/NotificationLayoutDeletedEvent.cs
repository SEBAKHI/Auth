using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>Raised when an application's notification layout was deleted.</summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="NotificationLayoutId">The deleted layout.</param>
/// <param name="ApplicationId">Its application. Never null: the global layout cannot be deleted.</param>
/// <param name="Channel">The channel it wrapped; that channel's emails now use the global layout.</param>
/// <param name="Name">Its name when it was deleted, the only trace left of it.</param>
/// <param name="TriggeredBy">The administrator who deleted it.</param>
/// <param name="OccurredAt">When it was removed.</param>
public sealed record NotificationLayoutDeletedEvent(
    Guid EventId,
    Guid NotificationLayoutId,
    Guid? ApplicationId,
    NotificationChannelType Channel,
    string Name,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
