using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents the browser's single-sign-on session with the identity provider
/// itself — the server-side counterpart of the IdP session cookie. One IdP
/// session can spawn many per-application <see cref="UserSession"/>s through
/// authorization-code exchanges. Only the HMAC-SHA256 hash of the cookie token
/// is stored, never the plain token.
/// </summary>
public class IdpSession : EntityBase
{
    /// <summary>
    /// Gets the ID of the user this session belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the session cookie token.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session expires (absolute lifetime).
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session was revoked (e.g. logout).
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Gets the IP address the session was created from.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the device/user-agent information the session was created with.
    /// </summary>
    public string? DeviceInfo { get; private set; }

    /// <summary>
    /// Gets the authentication methods this SSO session proved, in their stored
    /// form (<see cref="AuthenticationMethods"/> flags); null for a session
    /// recorded before the column existed. Written at sign-in and raised when a
    /// second factor is proved in a session that presented this cookie. Not read
    /// back yet: nothing decides on it until the authorization-code path carries
    /// it to the application session.
    /// </summary>
    public int? AuthMethods { get; private set; }

    /// <summary>
    /// Gets whether the person asked to stay signed in on this browser ("Stay
    /// signed in", OI-154): the cookie then outlives the browser and the session
    /// has no idle limit. False is a browser session, which ends with the browser
    /// and after the idle limit. A row recorded before the column existed reads
    /// false, so it idles out like a new browser session.
    /// </summary>
    public bool IsPersistent { get; private set; }

    /// <summary>
    /// Gets the UTC time this session was last used: to sign the person in to
    /// something, or through a refresh token born from it. Null until the first
    /// use, when <see cref="CreatedAt"/> stands in.
    /// </summary>
    public DateTime? LastActivityAt { get; private set; }

    /// <summary>
    /// Gets whether the session has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Gets the moment the idle limit counts from: the last use, or the sign-in.
    /// </summary>
    public DateTime LastUsedAt => LastActivityAt ?? CreatedAt;

    private IdpSession() : base()
    {
    }

    public IdpSession(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? revokedAt,
        string? ipAddress,
        string? deviceInfo,
        bool isPersistent = false,
        DateTime? lastActivityAt = null) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        IpAddress = ipAddress;
        DeviceInfo = deviceInfo;
        IsPersistent = isPersistent;
        LastActivityAt = lastActivityAt;
    }

    /// <summary>
    /// A new session, signed in at <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="id">
    /// The session's id, drawn by the caller before the sign-in's refresh token
    /// is written so that token can name it (OI-154, D-154-2 (b)).
    /// </param>
    /// <param name="userId">The user signed in.</param>
    /// <param name="tokenHash">The HMAC-SHA256 hash of the cookie token.</param>
    /// <param name="persistent">Whether the person asked to stay signed in.</param>
    /// <param name="lifetime">
    /// The absolute lifetime this kind of session gets; fixed here, never extended.
    /// </param>
    /// <param name="nowUtc">The current time, from the caller's clock.</param>
    /// <param name="ipAddress">The address the sign-in came from.</param>
    /// <param name="deviceInfo">The sign-in's user agent.</param>
    /// <param name="methods">What the sign-in proved.</param>
    public static IdpSession Create(
        Guid id,
        Guid userId,
        string tokenHash,
        bool persistent,
        TimeSpan lifetime,
        DateTime nowUtc,
        string? ipAddress,
        string? deviceInfo,
        AuthenticationMethods methods = default)
    {
        return new IdpSession(
            id,
            userId,
            tokenHash,
            nowUtc,
            nowUtc.Add(lifetime),
            revokedAt: null,
            ipAddress,
            deviceInfo,
            isPersistent: persistent)
        {
            AuthMethods = methods.ToStored()
        };
    }

    /// <summary>
    /// Whether the session has gone unused for <paramref name="idleLimit"/> at
    /// <paramref name="nowUtc"/>. Only a browser session idles out; a zero limit
    /// means none.
    /// </summary>
    public bool IsIdleAt(DateTime nowUtc, TimeSpan idleLimit) =>
        !IsPersistent && idleLimit > TimeSpan.Zero && nowUtc - LastUsedAt >= idleLimit;

    /// <summary>
    /// The one rule for "this session still signs the browser in": not revoked,
    /// not past its absolute lifetime, and not idle (OI-154). Every reader asks it
    /// through the IdP session reader, with the same clock and limit.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="idleLimit">The browser-session idle limit; zero for none.</param>
    public bool IsValidAt(DateTime nowUtc, TimeSpan idleLimit) =>
        !IsRevoked && nowUtc < ExpiresAt && !IsIdleAt(nowUtc, idleLimit);

    /// <summary>
    /// Records a use at <paramref name="nowUtc"/>, as the database recorded it.
    /// </summary>
    public void RecordActivity(DateTime nowUtc)
    {
        LastActivityAt = nowUtc;
    }

    /// <summary>
    /// Revokes the session (logout or security action).
    /// </summary>
    public void Revoke()
    {
        RevokedAt = DateTime.UtcNow;
    }
}
