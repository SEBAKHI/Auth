using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>Raised when a design became the one its scope's sign-in pages show.</summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="LoginDesignId">The design now active.</param>
/// <param name="ApplicationId">Its application; null for one of the platform's designs.</param>
/// <param name="Design">Its name, logo choice and version.</param>
/// <param name="PreviousActiveId">The design it replaced, deactivated in the same transaction; null when none was active.</param>
/// <param name="TriggeredBy">The administrator who activated it.</param>
/// <param name="OccurredAt">When the swap was stored.</param>
public sealed record LoginDesignActivatedEvent(
    Guid EventId,
    Guid LoginDesignId,
    Guid? ApplicationId,
    LoginDesignSnapshot Design,
    Guid? PreviousActiveId,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
