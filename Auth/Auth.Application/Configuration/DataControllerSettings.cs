namespace Auth.Application.Configuration;

/// <summary>
/// Identity of the data controller, as published in the privacy policy.
///
/// <para>
/// These are legal facts, not code. They used to live in a build-time constant
/// in the accounts SPA, which meant the authoritative copy — the policy document
/// served from the database — kept whatever text was baked in when the seed was
/// generated. Filling the constant changed the offline fallback and nothing
/// else. They are settings because they are edited by the operator, differ per
/// deployment, and are quoted verbatim in a document the law requires to name
/// the controller.
/// </para>
///
/// <para>
/// Defaults are deliberately EMPTY rather than bracketed placeholders: the
/// publish guard and the unfilled-policy banner both test for blank, and a
/// default that looks filled would satisfy neither.
/// </para>
/// </summary>
public class DataControllerSettings
{
    public const string SectionName = "DataController";

    /// <summary>Registered legal name of the controller (e.g. "Acme Corp LLC").</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>Registered address, exactly as it should appear in the policy.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>A monitored inbox for privacy and data-subject rights requests.</summary>
    public string PrivacyEmail { get; set; } = string.Empty;

    /// <summary>Email delivery provider, named in "who we share data with".</summary>
    public string EmailProvider { get; set; } = string.Empty;

    /// <summary>Hosting provider, named in "who we share data with".</summary>
    public string HostingProvider { get; set; } = string.Empty;

    /// <summary>
    /// Country where the service is hosted. BARE country name only — the
    /// Turkish and French policy sentences supply their own preposition and
    /// suffix around it.
    /// </summary>
    public string HostingCountry { get; set; } = string.Empty;

    /// <summary>
    /// Data protection officer contact. Optional: GDPR only requires one in
    /// specific cases, and the policy omits the line entirely when blank.
    /// </summary>
    public string DpoContact { get; set; } = string.Empty;

    /// <summary>
    /// VERBİS (Veri Sorumluları Sicili) registration number. Optional: fill it
    /// when the controller meets Türkiye's registration thresholds.
    /// </summary>
    public string VerbisNo { get; set; } = string.Empty;

    /// <summary>
    /// KEP (kayıtlı elektronik posta) address. Optional: a Turkish registered
    /// e-mail address is one of the application channels under the KVKK
    /// application communiqué.
    /// </summary>
    public string KepAddress { get; set; } = string.Empty;

    /// <summary>
    /// Field names that must be filled before a policy may be published. The
    /// three omitted ones are conditionally-required by law, so the system
    /// cannot decide for the operator whether they apply.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        nameof(LegalName),
        nameof(Address),
        nameof(PrivacyEmail),
        nameof(EmailProvider),
        nameof(HostingProvider),
        nameof(HostingCountry)
    ];

    /// <summary>
    /// Returns the required fields that are still blank or still hold a
    /// bracketed placeholder. Empty means the policy names a real controller.
    /// </summary>
    public IReadOnlyList<string> MissingRequired()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(LegalName)] = LegalName,
            [nameof(Address)] = Address,
            [nameof(PrivacyEmail)] = PrivacyEmail,
            [nameof(EmailProvider)] = EmailProvider,
            [nameof(HostingProvider)] = HostingProvider,
            [nameof(HostingCountry)] = HostingCountry
        };

        return RequiredFields
            .Where(field => string.IsNullOrWhiteSpace(values[field]) || values[field].Contains('['))
            .ToList();
    }
}
