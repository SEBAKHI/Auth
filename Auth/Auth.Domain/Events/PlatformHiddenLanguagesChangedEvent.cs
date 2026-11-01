using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the languages hidden from the language menus of both apps changed.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="SettingsId">The platform settings row.</param>
/// <param name="Before">The hidden language codes before the save; empty when none was hidden.</param>
/// <param name="After">The hidden language codes after the save; empty when none is hidden.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record PlatformHiddenLanguagesChangedEvent(
    Guid EventId,
    Guid SettingsId,
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
