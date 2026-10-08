using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a signed-in session proved the second factor (a step-up), so the
/// session counts as two-factor from its next refresh on.
/// </summary>
/// <remarks>
/// For the audit row only: no notice goes to the owner, because nothing about the
/// account changed — the session proved what it holds.
/// </remarks>
public record TwoFactorSteppedUpEvent(
    Guid UserId,
    Guid SessionId,
    SecondFactorMethod Method) : IDomainEvent;
