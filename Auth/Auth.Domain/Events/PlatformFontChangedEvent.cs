using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when one of the platform's fonts was uploaded, replaced or removed.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="SettingsId">The platform settings row.</param>
/// <param name="Role">Which font: heading or body.</param>
/// <param name="BeforeFamilyName">The family name of the font before the save; null when there was none or it declared none.</param>
/// <param name="AfterFamilyName">The family name of the font after the save; null when it was removed or declares none.</param>
/// <param name="BeforeKey">The stored file before the save; null when there was none.</param>
/// <param name="AfterKey">The stored file after the save; null when it was removed.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record PlatformFontChangedEvent(
    Guid EventId,
    Guid SettingsId,
    FontRole Role,
    string? BeforeFamilyName,
    string? AfterFamilyName,
    string? BeforeKey,
    string? AfterKey,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
