namespace Auth.Application.DTOs;

/// <summary>
/// Expiry posture of one credential family.
/// </summary>
public class CredentialExpiryBucketDto
{
    /// <summary>Live credentials expiring inside the horizon with no longer-lived successor.</summary>
    public int ExpiringCount { get; set; }

    /// <summary>Earliest expiry among the counted credentials; null when none are counted.</summary>
    public DateTime? SoonestExpiresAt { get; set; }

    /// <summary>Every live credential of this family.</summary>
    public int TotalActive { get; set; }
}

/// <summary>
/// Dashboard credential-expiry aggregates. A family the caller may not read is
/// null rather than zero: zero would assert that nothing is expiring, which is a
/// claim this response is not entitled to make.
/// </summary>
public class CredentialStatsDto
{
    /// <summary>Days ahead the expiry counts look.</summary>
    public int HorizonDays { get; set; }

    /// <summary>API keys; null when the caller lacks apikeys:read.</summary>
    public CredentialExpiryBucketDto? ApiKeys { get; set; }

    /// <summary>Webhook signing keys; null when the caller lacks webhookkeys:read.</summary>
    public CredentialExpiryBucketDto? WebhookKeys { get; set; }
}
