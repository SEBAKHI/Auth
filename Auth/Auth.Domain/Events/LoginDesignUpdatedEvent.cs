using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a design was saved. When <paramref name="IsActive"/> is true the change is what
/// the scope's sign-in pages show from now on: there is no publish step.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="LoginDesignId">The design.</param>
/// <param name="ApplicationId">Its application; null for one of the platform's designs.</param>
/// <param name="IsActive">Whether visitors see it.</param>
/// <param name="Before">The design before the save.</param>
/// <param name="After">The design after the save.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the save was stored.</param>
public sealed record LoginDesignUpdatedEvent(
    Guid EventId,
    Guid LoginDesignId,
    Guid? ApplicationId,
    bool IsActive,
    LoginDesignSnapshot Before,
    LoginDesignSnapshot After,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
