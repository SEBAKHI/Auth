using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a link between a user account and an external authentication provider.
/// One user can have multiple external logins (Google + Apple, etc.).
/// </summary>
public class UserExternalLogin : EntityBase
{
    /// <summary>
    /// Gets the ID of the linked user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the provider code (e.g., "google", "apple").
    /// </summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the user's unique ID from the provider (e.g., Google 'sub' claim).
    /// </summary>
    public string ProviderUserId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the email address from the provider.
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>
    /// Gets the display name from the provider.
    /// </summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Gets the profile picture URL from the provider.
    /// </summary>
    public string? PictureUrl { get; private set; }

    /// <summary>
    /// Gets the provider's refresh token (Apple), AES-256-GCM ciphertext under
    /// the user's per-user DEK. Stored solely for deletion-time revocation and
    /// crypto-shredded with the account.
    /// </summary>
    public string? ProviderRefreshTokenEnc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    private UserExternalLogin() : base()
    {
    }

    /// <summary>
    /// Constructor for Dapper mapping.
    /// </summary>
    public UserExternalLogin(
        Guid id,
        Guid userId,
        string provider,
        string providerUserId,
        string? email,
        string? name,
        string? pictureUrl,
        string? providerRefreshTokenEnc,
        DateTime createdAt,
        DateTime? modifiedAt) : base(id)
    {
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        Email = email;
        Name = name;
        PictureUrl = pictureUrl;
        ProviderRefreshTokenEnc = providerRefreshTokenEnc;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
    }

    /// <summary>
    /// Creates a new external login record.
    /// </summary>
    public static UserExternalLogin Create(
        Guid userId,
        string provider,
        string providerUserId,
        string? email = null,
        string? name = null,
        string? pictureUrl = null)
    {
        return new UserExternalLogin
        {
            UserId = userId,
            Provider = provider,
            ProviderUserId = providerUserId,
            Email = email,
            Name = name,
            PictureUrl = pictureUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates the cached provider info (name, picture may change over time).
    /// </summary>
    public void UpdateFromProvider(string? email, string? name, string? pictureUrl)
    {
        Email = email;
        Name = name;
        PictureUrl = pictureUrl;
        ModifiedAt = DateTime.UtcNow;
    }
}
