namespace Auth.Application.DTOs;

/// <summary>
/// One recorded privacy-policy revision with its notification stamp.
/// </summary>
public class PrivacyPolicyVersionDto
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime EffectiveDateUtc { get; set; }
    public bool IsPublished { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
    public int? NotifiedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Language codes that already have a stored document.</summary>
    public IReadOnlyList<string> Languages { get; set; } = [];
}

/// <summary>
/// Outcome of a policy-change notification run.
/// </summary>
public class PrivacyPolicyNotifyResultDto
{
    public int RecipientCount { get; set; }
}

/// <summary>
/// One language document of a policy revision.
/// </summary>
public class PrivacyPolicyContentDto
{
    public string Version { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string ContentJson { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// The published policy as the accounts app consumes it: the document for the
/// requested language plus the LIVE numeric disclosures, so the rendered text
/// can never contradict the running configuration.
/// </summary>
public class PublishedPrivacyPolicyDto
{
    public string Version { get; set; } = string.Empty;
    public DateTime EffectiveDateUtc { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string ContentJson { get; set; } = string.Empty;
    public PrivacyPolicyDisclosureDto Disclosure { get; set; } = new();
}

/// <summary>
/// Configuration-driven values quoted by the policy. Sourced from
/// <c>AccountDeletionSettings</c> at request time and substituted into the
/// document's <c>{{token}}</c> placeholders — change appsettings and the
/// published policy follows automatically.
/// </summary>
public class PrivacyPolicyDisclosureDto
{
    /// <summary>Recovery window before irreversible deletion (AccountDeletion:GraceDays).</summary>
    public int GraceDays { get; set; }

    /// <summary>Deletion OTP lifetime (AccountDeletion:OtpExpirationMinutes).</summary>
    public int OtpValidityMinutes { get; set; }

    /// <summary>Login-attempt retention (AccountDeletion:LoginAttemptRetentionDays).</summary>
    public int LoginAttemptRetentionDays { get; set; }

    /// <summary>Delivered-mail retention (AccountDeletion:OutboxRetentionDays).</summary>
    public int OutboxRetentionDays { get; set; }

    /// <summary>Current policy version (AccountDeletion:PolicyVersion).</summary>
    public string PolicyVersion { get; set; } = string.Empty;
}
