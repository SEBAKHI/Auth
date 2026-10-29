using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the active design was switched off without another taking its place: an
/// application's pages fall back to the platform's design, the platform's to the plain panel.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="LoginDesignId">The design no longer shown.</param>
/// <param name="ApplicationId">Its application; null for one of the platform's designs.</param>
/// <param name="Design">Its name, logo choice and version.</param>
/// <param name="TriggeredBy">The administrator who deactivated it.</param>
/// <param name="OccurredAt">When it was stored.</param>
public sealed record LoginDesignDeactivatedEvent(
    Guid EventId,
    Guid LoginDesignId,
    Guid? ApplicationId,
    LoginDesignSnapshot Design,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
