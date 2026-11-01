using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>Raised when a draft privacy-policy revision was deleted (only a draft nobody was shown can be).</summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="PrivacyPolicyVersionId">The deleted revision.</param>
/// <param name="Version">Its "YYYY.MM" identifier, the only trace left of it.</param>
/// <param name="TriggeredBy">The administrator who deleted it.</param>
/// <param name="OccurredAt">When it was removed.</param>
public sealed record PrivacyPolicyVersionDeletedEvent(
    Guid EventId,
    Guid PrivacyPolicyVersionId,
    string Version,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
