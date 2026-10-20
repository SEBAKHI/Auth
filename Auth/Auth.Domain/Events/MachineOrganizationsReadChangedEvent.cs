using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an administrator switched an application's machine directory consent on or off
/// (OI-202): whether its own server, with its machine key and no user, may read the directory of
/// every organization that enabled it (the machine scope <c>organizations.read.all</c>).
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApplicationId">The application whose consent changed.</param>
/// <param name="Allowed">The consent's new value.</param>
/// <param name="TriggeredBy">The administrator who changed it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record MachineOrganizationsReadChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    bool Allowed,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
