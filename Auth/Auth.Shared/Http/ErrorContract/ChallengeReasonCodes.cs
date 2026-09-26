using System.Collections.Frozen;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// Why a 401 was returned, when the reason tells the client what to do next (ADR 0001): an
/// expired access token is refreshed, a revoked one or a revoked session means signing in again.
/// Recorded in <see cref="ProblemItems.Code"/> in place of <see cref="TransportErrorCodes.Unauthenticated"/>.
/// </summary>
public static class ChallengeReasonCodes
{
    /// <summary>The bearer token's lifetime has passed (JwtBearer challenge).</summary>
    public const string TokenExpired = "Http.TokenExpired";

    /// <summary>The bearer token was revoked before it expired.</summary>
    public const string TokenRevoked = "Http.TokenRevoked";

    /// <summary>The session the bearer token belongs to was revoked.</summary>
    public const string SessionRevoked = "Http.SessionRevoked";

    /// <summary>Every challenge reason, for the contract test.</summary>
    public static readonly FrozenSet<string> All =
        new[] { TokenExpired, TokenRevoked, SessionRevoked }.ToFrozenSet(StringComparer.Ordinal);
}
