using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the height an application's logo is shown at on its sign-in pages changed.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApplicationId">The application whose logo height changed.</param>
/// <param name="ApplicationCode">Its code (<c>client_id</c>).</param>
/// <param name="BeforePx">The chosen height before the save; null when none was chosen (the default).</param>
/// <param name="AfterPx">The chosen height after the save; null when it went back to the default.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record ApplicationLogoHeightChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    string ApplicationCode,
    int? BeforePx,
    int? AfterPx,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
