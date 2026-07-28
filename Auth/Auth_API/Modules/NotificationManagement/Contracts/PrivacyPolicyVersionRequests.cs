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

    /// <summary>Note describing what changed in this revision.</summary>
    public string? ChangeNote { get; init; }
}

/// <summary>
/// Request to update a revision's editable metadata.
/// </summary>
public record UpdatePrivacyPolicyVersionRequest
{
    /// <summary>The revision identifier in "YYYY.MM" format.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// New identifier. Omit to keep the current one; only an unannounced
    /// draft may be renamed.
    /// </summary>
    public string? NewVersion { get; init; }

    /// <summary>UTC instant the revision takes effect.</summary>
    public DateTime EffectiveDateUtc { get; init; }

    /// <summary>Note describing what changed in this revision.</summary>
    public string? ChangeNote { get; init; }
}

/// <summary>
/// Request naming one revision. The version travels in the body — dotted route
/// segments ("2026.07") are unreliable under IIS static-file handling.
/// </summary>
public record PrivacyPolicyVersionRequest
{
    /// <summary>The revision identifier in "YYYY.MM" format.</summary>
    public string Version { get; init; } = string.Empty;
}

/// <summary>
/// Request to save one language document of a revision.
/// </summary>
public record SavePrivacyPolicyContentRequest
{
    /// <summary>The revision identifier in "YYYY.MM" format.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>ISO language code of the document.</summary>
    public string LanguageCode { get; init; } = string.Empty;

    /// <summary>The document JSON (validated server-side before storage).</summary>
    public string ContentJson { get; init; } = string.Empty;
}
