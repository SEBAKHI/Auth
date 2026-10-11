using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an administrator revoked an application's machine key (OI-128). A machine token the
/// key already obtained keeps working until it expires, within five minutes.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="MachineKeyId">The revoked key.</param>
/// <param name="ApplicationId">The application the key authenticated.</param>
/// <param name="Kid">The key's identifier.</param>
/// <param name="TriggeredBy">The administrator who revoked the key.</param>
/// <param name="OccurredAt">When the key was revoked.</param>
public sealed record MachineKeyRevokedEvent(
    Guid EventId,
    Guid MachineKeyId,
    Guid ApplicationId,
    string Kid,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
