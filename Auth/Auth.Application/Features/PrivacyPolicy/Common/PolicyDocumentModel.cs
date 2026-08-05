using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// Server-side mirror of the authored document shape stored in
/// <c>PrivacyPolicyTranslations.ContentJson</c> (the frontend's
/// <c>PrivacyPolicyContent</c>).
///
/// Every property is required by the renderer, so a field added to the contract
/// without a matching field here is caught by
/// <c>PolicyDocumentRendererTests</c> rather than by a section quietly missing
/// from a published legal document.
/// </summary>
public sealed class PolicyDocumentModel
{
    public string Title { get; set; } = string.Empty;
    public string EffectiveDate { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public List<string> Intro { get; set; } = [];
    public List<PolicyDocumentSection> Sections { get; set; } = [];
    public PolicyDocumentRetention Retention { get; set; } = new();
    public PolicyDocumentDeletion Deletion { get; set; } = new();
    public List<PolicyDocumentSection> Rights { get; set; } = [];
    public List<PolicyDocumentSection> Closing { get; set; } = [];
    public string ContactDpoLabel { get; set; } = string.Empty;
    public string ContactVerbisLabel { get; set; } = string.Empty;
    public string ContactKepLabel { get; set; } = string.Empty;

    /// <summary>
    /// The authoring-time banner warning that the controller is unnamed.
    ///
    /// It is never rendered into a published artifact: publishing is refused
    /// while the controller is incomplete, so on the public surface the banner
    /// is unreachable rather than merely suppressed. The console preview still
    /// shows it, because there it describes an unsaved draft.
    /// </summary>
    public string UnfilledWarning { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Parses a stored document, or null when it is not readable.</summary>
    public static PolicyDocumentModel? TryParse(string contentJson)
    {
        try
        {
            return JsonSerializer.Deserialize<PolicyDocumentModel>(
                contentJson, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class PolicyDocumentSection
{
    public string Heading { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = [];
    public List<string>? Bullets { get; set; }
}

public sealed class PolicyDocumentRetention
{
    public string Heading { get; set; } = string.Empty;
    public string Intro { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public List<PolicyDocumentRetentionRow> Rows { get; set; } = [];
}

public sealed class PolicyDocumentRetentionRow
{
    public string Category { get; set; } = string.Empty;
    public string Retention { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class PolicyDocumentDeletion
{
    public string Heading { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = [];
    public List<string> Bullets { get; set; } = [];
    public string Button { get; set; } = string.Empty;
    public string SignedInHint { get; set; } = string.Empty;
}
