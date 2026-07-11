using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auth_API.Common;

/// <summary>
/// Serializes <see cref="DateTime"/> values as UTC ISO-8601 with a trailing "Z".
/// Database timestamps are stored in UTC but Dapper materializes them with
/// <see cref="DateTimeKind.Unspecified"/>, which System.Text.Json would emit
/// without an offset — clients would then interpret the UTC wall-clock as local
/// time. Incoming values are normalized to UTC.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return DateTime.Parse(
            value!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc);
    }
}

/// <summary>
/// Nullable counterpart of <see cref="UtcDateTimeConverter"/>.
/// </summary>
public class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null
            ? null
            : Inner.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            Inner.Write(writer, value.Value, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
