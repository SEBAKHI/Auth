using System.Text.Json;

namespace Auth.Application.SystemSettings;

/// <summary>
/// Flattens a sparse nested-JSON override object into configuration-style
/// paths. Used with <c>expandArrays: false</c> by the write path (an array
/// is one field to validate as a whole) and with <c>expandArrays: true</c>
/// by the database configuration provider (configuration keys address array
/// elements by index).
/// </summary>
public static class JsonOverrideFlattener
{
    public static IReadOnlyList<KeyValuePair<string, JsonElement>> Flatten(
        JsonElement root,
        bool expandArrays)
    {
        var results = new List<KeyValuePair<string, JsonElement>>();
        Walk(root, prefix: string.Empty, expandArrays, results);
        return results;
    }

    private static void Walk(
        JsonElement element,
        string prefix,
        bool expandArrays,
        List<KeyValuePair<string, JsonElement>> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}";
                    Walk(property.Value, path, expandArrays, results);
                }

                break;

            case JsonValueKind.Array when expandArrays:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{prefix}:{index}", expandArrays, results);
                    index++;
                }

                break;

            default:
                // Leaf (string/number/bool/null) — or the whole array when
                // expandArrays is false.
                results.Add(new KeyValuePair<string, JsonElement>(prefix, element));
                break;
        }
    }
}
