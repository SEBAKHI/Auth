using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// A webhook key validated against a fingerprint computed under a key that no longer
/// issues (a previous or legacy HMAC key, S11 D-T3). Its handler rewrites the stored
/// fingerprint to the issuing form, conditionally, so the key keeps working after the
/// old key's 90-day grace without its integrator doing anything.
/// </summary>
/// <remarks>
/// The fingerprints are not the key: they are what the database already stores, and
/// this event never leaves the process or reaches a log.
/// </remarks>
/// <param name="WebhookKeyId">The webhook key that validated.</param>
/// <param name="MatchedHash">The stored fingerprint it matched, exactly as stored.</param>
/// <param name="NewHash">Its fingerprint under the issuing key.</param>
public record WebhookKeyHashUpgradedEvent(
    Guid WebhookKeyId,
    string MatchedHash,
    string NewHash) : IDomainEvent;
