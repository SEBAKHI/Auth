using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when staged destruction of an account has finished. Email and
/// DisplayName are pre-destruction snapshots whose only remaining use is the
/// final notification; the audit trail must record neither.
/// </summary>
/// <param name="UserId">The destroyed account's immutable id.</param>
/// <param name="Email">Snapshot address for the final notice.</param>
/// <param name="DisplayName">Snapshot display name for the final notice.</param>
/// <param name="PolicyVersion">Retention-policy version applied.</param>
/// <param name="ExternalRevocationFailed">
/// True when revoking tokens at an external identity provider (e.g., Apple)
/// did not succeed before destruction completed; recorded in the destruction
/// audit detail.
/// </param>
public record AccountDeletionCompletedEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    string PolicyVersion,
    bool ExternalRevocationFailed) : IDomainEvent;
