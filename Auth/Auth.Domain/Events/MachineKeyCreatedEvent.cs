using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application received a new machine key (OI-128). Carries the key's identifier,
/// never its material.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="MachineKeyId">The new key.</param>
/// <param name="ApplicationId">The application the key authenticates.</param>
/// <param name="Kid">The key's identifier, which the client assertion names.</param>
/// <param name="Generated">True when the server generated the pair; false for a pasted public key.</param>
/// <param name="TriggeredBy">The administrator who created the key.</param>
/// <param name="OccurredAt">When the key was stored.</param>
public sealed record MachineKeyCreatedEvent(
    Guid EventId,
    Guid MachineKeyId,
    Guid ApplicationId,
    string Kid,
    bool Generated,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
