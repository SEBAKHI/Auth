namespace Auth.Application.Configuration;

/// <summary>
/// How long a revocation has to be kept so that no token it covers outlives it (X04, RV1).
/// </summary>
/// <remarks>
/// <para>
/// An access token is accepted until its exp plus the clock skew validation allows. Both are
/// bounded where they are read (<see cref="JwtSettings.AccessTokenLifetime"/> and
/// <see cref="JwtSettings.ClockSkew"/>), so the longest any token can be accepted after the
/// instant it was issued is the largest lifetime plus the largest skew: 24 h 5 min. A constant,
/// not the lifetime in force, so lowering the lifetime never shortens a revocation that tokens
/// issued under the old lifetime still depend on.
/// </para>
/// <para>
/// Readers: the user-level entry of the revocation list, the floor of a session entry, and the
/// window a signing key that stops signing is still accepted (<c>SigningKeyOverlap</c>).
/// </para>
/// </remarks>
public static class RevocationHorizon
{
    /// <summary>
    /// The longest a token issued at or before a user-level revocation can still be accepted
    /// after it: <see cref="JwtSettings.MaxAccessTokenLifetimeMinutes"/> plus
    /// <see cref="JwtSettings.MaxClockSkewSeconds"/>.
    /// </summary>
    public static readonly TimeSpan User =
        TimeSpan.FromMinutes(JwtSettings.MaxAccessTokenLifetimeMinutes) + JwtSettings.MaxClockSkew;
}
