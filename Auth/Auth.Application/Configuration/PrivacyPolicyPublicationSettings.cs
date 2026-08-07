namespace Auth.Application.Configuration;

/// <summary>
/// Filesystem location that holds the privacy-policy documents served by the
/// Accounts site's <c>/privacy</c> virtual directory.
/// </summary>
public sealed class PrivacyPolicyPublicationSettings
{
    public const string SectionName = "PrivacyPolicyPublication";

    /// <summary>
    /// Absolute path, or a path relative to the API base directory, where the
    /// public HTML files are written. Production points this at storage outside
    /// every application's deployment directory.
    /// </summary>
    public string PhysicalPath { get; set; } = "App_Data/privacy-policy-public";
}
