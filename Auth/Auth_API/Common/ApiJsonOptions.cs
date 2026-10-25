using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auth_API.Common;

/// <summary>
/// How every response body is written: camelCase, nulls omitted, enums by name, and
/// timestamps as UTC with a trailing "Z". One place, applied by Program.cs to the
/// controllers' JSON options, so a test serializes with exactly what the API writes.
/// </summary>
public static class ApiJsonOptions
{
    /// <summary>Applies the API's settings to <paramref name="options"/>.</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // An optional member a client does not know is simply absent (twoFactorMethods,
        // twoFactorEmailCode): an older client reads the body it always read.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter());
        // DB timestamps are UTC but arrive as Kind.Unspecified from Dapper;
        // these converters guarantee offset-qualified ("Z") ISO-8601 output.
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new NullableUtcDateTimeConverter());
    }
}
