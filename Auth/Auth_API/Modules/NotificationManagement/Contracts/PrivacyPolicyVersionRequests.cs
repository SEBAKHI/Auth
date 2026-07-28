namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to record a new privacy-policy revision.
/// </summary>
public record CreatePrivacyPolicyVersionRequest
{
    /// <summary>The revision identifier in "YYYY.MM" format.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>UTC instant the revision takes effect.</summary>
    public DateTime EffectiveDateUtc { get; init; }
}

/// <summary>
/// Request to send the policy-change notice for a recorded revision to every
/// active user. The version travels in the body — dotted route segments
/// ("2026.07") are unreliable under IIS static-file handling.
/// </summary>
public record NotifyPrivacyPolicyVersionRequest
{
    /// <summary>The revision identifier in "YYYY.MM" format.</summary>
    public string Version { get; init; } = string.Empty;
}
