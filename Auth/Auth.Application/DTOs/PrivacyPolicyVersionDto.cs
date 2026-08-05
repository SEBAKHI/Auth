namespace Auth.Application.DTOs;

/// <summary>
/// One recorded privacy-policy revision with its notification stamp.
/// </summary>
public class PrivacyPolicyVersionDto
{
    /// <summary>
    /// True when this revision is published but the values frozen into its
    /// document no longer match the running configuration — so the console can
    /// prompt a re-publish instead of the system silently amending a notice
    /// people have already been shown.
    /// </summary>
    public bool DisclosureOutOfDate { get; set; }

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
/// The document actually served to the public: bytes rendered when the revision
/// was published, plus the identity needed to serve them correctly.
/// </summary>
public class PolicyDocumentDto
{
    /// <summary>Complete standalone HTML — own head, inline styles, no script.</summary>
    public string Html { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="Html"/>; used as the strong ETag.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Base64 SHA-256 of the document's inline stylesheet, sent as
    /// <c>style-src 'sha256-…'</c> so the page can style itself under a policy
    /// that otherwise permits nothing.
    /// </summary>
    public string StyleHash { get; set; } = string.Empty;

    /// <summary>The language this document is served as.</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// The language the body was written in. Differs from
    /// <see cref="LanguageCode"/> on a disclosed fallback, which the document
    /// states in the reader's own language.
    /// </summary>
    public string SourceLanguageCode { get; set; } = string.Empty;

    public DateTime RenderedAt { get; set; }
}

/// <summary>
/// The published policy as the console's editor preview consumes it: the
/// authored document for a language plus the LIVE disclosures.
///
/// Not the public read path — that serves <see cref="PolicyDocumentDto"/>.
/// Preview needs the live values precisely so an operator can see how the next
/// publish would differ from the one currently served.
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

    /// <summary>
    /// How long a deleted email stays blocked from re-registration
    /// (AccountDeletion:IdentifierReservationDays, floored at the audit-log
    /// retention). Quoted by the policy, so it must be the EFFECTIVE value —
    /// publishing the raw setting would understate the real window.
    /// </summary>
    public int IdentifierReservationDays { get; set; }

    /// <summary>Current policy version (AccountDeletion:PolicyVersion).</summary>
    public string PolicyVersion { get; set; } = string.Empty;

    // ── Data-controller identity (DataController:*) ─────────────────────────
    // Every one of these is non-nullable with an empty default on purpose. A
    // JSON null reaches the client-side interpolate() as String(null) and would
    // render the literal word "null" inside a published legal disclosure.

    /// <summary>Registered legal name of the controller.</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>Registered address of the controller.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Monitored inbox for privacy and rights requests.</summary>
    public string PrivacyEmail { get; set; } = string.Empty;

    /// <summary>Email delivery provider named in the sharing section.</summary>
    public string EmailProvider { get; set; } = string.Empty;

    /// <summary>Hosting provider named in the sharing section.</summary>
    public string HostingProvider { get; set; } = string.Empty;

    /// <summary>Bare hosting country name (sentences supply their own preposition).</summary>
    public string HostingCountry { get; set; } = string.Empty;

    /// <summary>Data protection officer contact; blank omits the line.</summary>
    public string DpoContact { get; set; } = string.Empty;

    /// <summary>VERBİS registration number; blank omits the line.</summary>
    public string VerbisNo { get; set; } = string.Empty;

    /// <summary>KEP address; blank omits the line.</summary>
    public string KepAddress { get; set; } = string.Empty;
}
