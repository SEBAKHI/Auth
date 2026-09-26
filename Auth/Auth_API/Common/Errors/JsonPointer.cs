using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Auth_API.Common.Errors;

/// <summary>
/// Turns a validator's property path (<c>Translations[0].LanguageCode</c>) into the RFC 6901
/// pointer of the request-body member it names (<c>#/translations/0/languageCode</c>), using the
/// body type's JSON contract, so the names are the ones the client sent.
/// </summary>
internal static class JsonPointer
{
    private static readonly char[] Separators = ['.', '[', ']'];

    /// <summary>
    /// The pointer, or <c>null</c> when a segment of the path is not a member of the body: a
    /// command property the request does not carry under that name gets no pointer rather than
    /// a wrong one.
    /// </summary>
    public static string? For(string propertyPath, Type bodyType, JsonSerializerOptions options)
    {
        var typeInfo = options.GetTypeInfo(bodyType);
        var segments = new List<string>();

        foreach (var segment in propertyPath.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (typeInfo.Kind == JsonTypeInfoKind.Enumerable
                && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                typeInfo = options.GetTypeInfo(typeInfo.ElementType!);
                segments.Add(segment);
                continue;
            }

            var property = typeInfo.Kind == JsonTypeInfoKind.Object
                ? typeInfo.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo member && member.Name == segment)
                : null;
            if (property is null)
            {
                return null;
            }

            segments.Add(property.Name);
            typeInfo = options.GetTypeInfo(property.PropertyType);
        }

        return segments.Count == 0
            ? null
            : "#/" + string.Join('/', segments.Select(segment =>
                Uri.EscapeDataString(segment.Replace("~", "~0").Replace("/", "~1"))));
    }
}
