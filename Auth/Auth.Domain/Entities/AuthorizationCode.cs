using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a one-time OAuth 2.0 authorization code issued by the authorize
/// endpoint and redeemed at the token endpoint (authorization-code + PKCE flow).
/// Only the HMAC-SHA256 hash of the code is stored, never the plain code.
/// </summary>
public class AuthorizationCode : EntityBase
{
    /// <summary>
    /// Gets the ID of the application (OAuth client) the code was issued to.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the ID of the user who authorized the request.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the code for secure lookups.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the exact redirect URI the code was issued for. The token request
    /// must present the identical value (RFC 6749 §4.1.3).
    /// </summary>
    public string RedirectUri { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the PKCE code challenge (base64url SHA-256 of the verifier, S256).
    /// </summary>
    public string CodeChallenge { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the code was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the code expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the code was redeemed. A consumed code can
    /// never be redeemed again.
    /// </summary>
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>
    /// Gets the IP address the authorize request came from.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the session the successful exchange produced, when one completed.
    /// </summary>
    /// <remarks>
    /// The only link between a code and what it minted, and therefore the only
    /// way to honour RFC 6749 4.1.2 when the same code is presented twice: it
    /// names exactly what has to be revoked. Null means the exchange never got
    /// that far, which the replay path treats as nothing to revoke.
    /// </remarks>
    public Guid? IssuedSessionId { get; private set; }

    /// <summary>
    /// Gets the scopes granted with the code, in canonical text: what the
    /// request asked for that the application is allowed, plus <c>openid</c>.
    /// The exchange issues this grant, narrowed to what the application is
    /// allowed at that moment. Null on a code minted before
    /// scopes existed; see <see cref="GrantedScopes"/>.
    /// </summary>
    public string? Scope { get; private set; }

    /// <summary>
    /// Gets the IdP session the authorize request was answered from (OI-154). The
    /// exchange copies it onto the application's refresh token, so that token's
    /// use counts as the session's (owner decision D-154-2 (b)). Null on a code
    /// issued before the column existed.
    /// </summary>
    public Guid? IdpSessionId { get; private set; }

    /// <summary>
    /// Gets whether the code was issued to one of the application's server-side redirect URIs
    /// (OI-171), so only a request that authenticates the application with its sign-in key may
    /// redeem it, and the refresh token it yields is bound to that key. False on a public code,
    /// and on every code written before the column.
    /// </summary>
    public bool RequiresClientAuthentication { get; private set; }

    /// <summary>
    /// Gets the User-Agent of the browser request that produced the code: the authorize
    /// request, or its resume (OI-178). Empty when that request sent none; null only on a code
    /// written before the column.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Gets the device identifier the person's own sign-in sent, taken from the IdP session
    /// the code was issued from (OI-178): an authorize navigation carries none. Null when that
    /// sign-in sent none, or on a code written before the column.
    /// </summary>
    public string? DeviceId { get; private set; }

    /// <summary>
    /// Gets the grant as a set. A code without <see cref="Scope"/> (minted by the
    /// previous build during a deploy) is granted <c>openid</c> only.
    /// </summary>
    public ScopeSet GrantedScopes => ScopeSet.FromStored(Scope);

    /// <summary>
    /// Gets whether the code has been redeemed.
    /// </summary>
    public bool IsConsumed => ConsumedAt.HasValue;

    private AuthorizationCode() : base()
    {
    }

    public AuthorizationCode(
        Guid id,
        Guid applicationId,
        Guid userId,
        string codeHash,
        string redirectUri,
        string codeChallenge,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? consumedAt,
        string? ipAddress,
        Guid? issuedSessionId = null,
        string? scope = null,
        Guid? idpSessionId = null,
        bool requiresClientAuthentication = false,
        string? userAgent = null,
        string? deviceId = null) : base(id)
    {
        ApplicationId = applicationId;
        UserId = userId;
        CodeHash = codeHash;
        RedirectUri = redirectUri;
        CodeChallenge = codeChallenge;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        IpAddress = ipAddress;
        IssuedSessionId = issuedSessionId;
        Scope = scope;
        IdpSessionId = idpSessionId;
        RequiresClientAuthentication = requiresClientAuthentication;
        UserAgent = userAgent;
        DeviceId = deviceId;
    }

    public static AuthorizationCode Create(
        Guid applicationId,
        Guid userId,
        string codeHash,
        string redirectUri,
        string codeChallenge,
        TimeSpan lifetime,
        string? ipAddress,
        ScopeSet grantedScopes,
        Guid? idpSessionId = null,
        bool requiresClientAuthentication = false,
        string? userAgent = null,
        string? deviceId = null)
    {
        return new AuthorizationCode
        {
            ApplicationId = applicationId,
            UserId = userId,
            CodeHash = codeHash,
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            IpAddress = ipAddress,
            Scope = grantedScopes.Value,
            IdpSessionId = idpSessionId,
            RequiresClientAuthentication = requiresClientAuthentication,
            UserAgent = userAgent,
            DeviceId = deviceId
        };
    }

    /// <summary>
    /// Where the sign-in this code completes came from (OI-178): the person's browser as
    /// this server saw it at authorize when the redeeming client authenticated, otherwise
    /// the token request itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client that authenticated holds an application key, so it is a server redeeming the
    /// code on the person's behalf, and its own address and user agent describe that server.
    /// The browser is the request that produced the code. A public client's token request
    /// comes from the browser, so it is answered from that request, exactly as before.
    /// </para>
    /// <para>
    /// A code with a null user agent was written before the column: it answers from the token
    /// request, as every code did then. Authorize writes an empty one when the browser sent
    /// none, so leaving the header out still records the browser's address, never the server's.
    /// </para>
    /// </remarks>
    /// <param name="tokenRequest">The token request's own address, user agent and device id.</param>
    /// <param name="clientAuthenticated">Whether the redeeming request authenticated an application key.</param>
    public SignInOrigin OriginFor(SignInOrigin tokenRequest, bool clientAuthenticated) =>
        clientAuthenticated && UserAgent is not null
            ? new SignInOrigin(IpAddress, UserAgent.Length == 0 ? null : UserAgent, DeviceId)
            : tokenRequest;

    /// <summary>
    /// Checks if the code has expired.
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Checks if the code can still be redeemed (not expired and not consumed).
    /// </summary>
    public bool IsValid()
    {
        return !IsConsumed && !IsExpired();
    }
}
