using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised by an application when its two-step verification requirement is switched on or
/// off (S23, AM11): at creation with the requirement on, and at any update that changes it.
/// The requirement decides who can sign in to the application, so each change earns its
/// own audit line rather than disappearing inside a settings save.
/// </summary>
/// <param name="EventId">This event's identifier.</param>
/// <param name="ApplicationId">The application whose requirement changed.</param>
/// <param name="ApplicationCode">The application's code, for the audit line.</param>
/// <param name="RequireTwoFactor">The requirement as it now stands.</param>
/// <param name="TriggeredBy">The administrator who saved the change.</param>
/// <param name="OccurredAt">When the change was made.</param>
public record ApplicationTwoFactorRequirementChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    string ApplicationCode,
    bool RequireTwoFactor,
    Guid TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
