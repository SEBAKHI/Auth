namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for webhook key information.
/// </summary>
public class WebhookKeyDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>Name of the owning application; null when unresolved.</summary>
    public string? ApplicationName { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Why the key was revoked, as the revoking administrator wrote it; null when not revoked, when no
    /// reason was given, or when the system revoked it (<see cref="RevokeReasonCode"/>).
    /// </summary>
    public string? RevokeReason { get; set; }

    /// <summary>
    /// Why the system revoked the key, as a code the reader's language names: one of
    /// <see cref="Auth.Domain.Constants.KeyRevocationReasons.All"/> (today only <c>ApplicationDeleted</c>).
    /// Null when an administrator revoked it, or it is not revoked.
    /// </summary>
    public string? RevokeReasonCode { get; set; }

    /// <summary>
    /// The key's stored fingerprint was computed under an HMAC key that no longer issues
    /// (after a keyring rotation, S11). Its next successful use re-fingerprints it under
    /// the current key and clears this; a key not used before <see cref="ReissueBy"/>
    /// stops validating there and must be rotated.
    /// </summary>
    public bool NeedsReissue { get; set; }

    /// <summary>
    /// When <see cref="NeedsReissue"/>: the end of the old HMAC key's 90-day grace, after
    /// which the key no longer validates; null when the old key is already gone.
    /// </summary>
    public DateTime? ReissueBy { get; set; }
}

/// <summary>
/// Response when creating a webhook key (includes the actual key - only shown once).
/// </summary>
public class CreateWebhookKeyResponse
{
    public Guid Id { get; set; }
    public string WebhookKey { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Response when rotating a webhook key.
/// </summary>
public class RotateWebhookKeyResponse
{
    public string NewWebhookKey { get; set; } = string.Empty;
    public Guid NewWebhookKeyId { get; set; }
    public string NewKeyPrefix { get; set; } = string.Empty;
    public DateTime? OldKeyExpiresAt { get; set; }
    public Guid OldWebhookKeyId { get; set; }
    public string Message { get; set; } = string.Empty;
}
