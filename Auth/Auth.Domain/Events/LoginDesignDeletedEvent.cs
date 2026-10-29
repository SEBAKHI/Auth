using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>Raised when an inactive design was deleted.</summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="LoginDesignId">The deleted design.</param>
/// <param name="ApplicationId">Its application; null for one of the platform's designs.</param>
/// <param name="Design">Its name, logo choice and version when it was deleted.</param>
/// <param name="TriggeredBy">The administrator who deleted it.</param>
/// <param name="OccurredAt">When it was removed.</param>
public sealed record LoginDesignDeletedEvent(
    Guid EventId,
    Guid LoginDesignId,
    Guid? ApplicationId,
    LoginDesignSnapshot Design,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
