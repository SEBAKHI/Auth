using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the panel beside the sign-in form was shown or hidden.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="SettingsId">The platform settings row.</param>
/// <param name="Shown">Whether the panel is shown after the save.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record PlatformLoginPanelChangedEvent(
    Guid EventId,
    Guid SettingsId,
    bool Shown,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
