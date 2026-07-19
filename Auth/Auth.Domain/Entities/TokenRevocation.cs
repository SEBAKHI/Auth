using Auth.Domain.Enums;

namespace Auth.Domain.Entities;

/// <summary>
/// A durable record that a token, session, or a user's token generation was
/// revoked. Backs the in-memory token blacklist so revocations survive restarts.
/// </summary>
/// <param name="Type">What was revoked.</param>
/// <param name="Key">jti (Token), sid (Session), or userId (User).</param>
/// <param name="EffectiveAt">
/// For <see cref="RevocationType.User"/>, tokens issued at or before this are
/// rejected; for Token/Session it is the creation time.
/// </param>
/// <param name="ExpiresAt">When the entry may be purged (revoked item can no longer be valid).</param>
public record TokenRevocation(
    RevocationType Type,
    string Key,
    DateTime EffectiveAt,
    DateTime ExpiresAt);
