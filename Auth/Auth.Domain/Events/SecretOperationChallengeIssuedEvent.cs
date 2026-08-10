using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// A step-up confirmation was raised against a destructive secret operation.
/// Recorded even though nothing was rotated: the attempt is the interesting
/// event when it was not the named administrator who made it.
/// </summary>
/// <param name="ChallengeId">The challenge that was raised.</param>
/// <param name="Operation">The operation it would authorize.</param>
/// <param name="RequestedBy">The administrator who requested it.</param>
/// <param name="IpAddress">The client address the request came from.</param>
public record SecretOperationChallengeIssuedEvent(
    Guid ChallengeId,
    SecretOperation Operation,
    Guid RequestedBy,
    string? IpAddress) : IDomainEvent;
