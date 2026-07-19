using Auth.Domain.Primitives;

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
        string? ipAddress) : base(id)
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
    }

    public static AuthorizationCode Create(
        Guid applicationId,
        Guid userId,
        string codeHash,
        string redirectUri,
        string codeChallenge,
        TimeSpan lifetime,
        string? ipAddress)
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
            IpAddress = ipAddress
        };
    }

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
