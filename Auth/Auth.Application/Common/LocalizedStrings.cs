using System.Text.Json;
using Auth.Domain.Constants;

namespace Auth.Application.Common;

/// <summary>
/// Picks one language's strings out of a per-language strings document
/// (<c>{ "&lt;lang&gt;": { "&lt;key&gt;": "…" } }</c>), the shape a notification layout keeps its
/// chrome strings in and a sign-in page design its texts.
/// </summary>
/// <remarks>
/// The fallback chain is the one every send relies on: the language asked for, then the
/// default language, then the first language in ordinal order. A missing translation never
/// leaves a page or a message without text.
/// </remarks>
public static class LocalizedStrings
{
    /// <summary>The strings for <paramref name="language"/>, with the fallback chain.</summary>
    /// <exception cref="JsonException">The document is not JSON.</exception>
    public static Dictionary<string, string> Pick(string stringsJson, string language)
    {
        using var document = JsonDocument.Parse(stringsJson);
        return Pick(document, language);
    }

    /// <summary>The strings for <paramref name="language"/>, with the fallback chain.</summary>
    public static Dictionary<string, string> Pick(JsonDocument document, string language)
    {
        var root = document.RootElement;

        JsonElement? Find(string lang) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(lang, out var element) &&
            element.ValueKind == JsonValueKind.Object
                ? element
                : null;

        var selected = Find(language)
            ?? Find(Languages.Default)
            ?? (root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => (JsonElement?)p.Value)
                    .FirstOrDefault()
                : null);

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selected is { } element)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    strings[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return strings;
    }
}
