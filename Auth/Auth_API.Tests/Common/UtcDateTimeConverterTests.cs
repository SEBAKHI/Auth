using System.Text.Json;
using Auth_API.Common;

namespace Auth_API.Tests.Common;

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new NullableUtcDateTimeConverter());
        return options;
    }

    [Fact]
    public void Write_UnspecifiedKind_SerializesWithZSuffix()
    {
        // Dapper materializes DB timestamps with Kind.Unspecified.
        var value = new DateTime(2026, 7, 4, 22, 1, 0, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(value, Options);

        json.Should().Be("\"2026-07-04T22:01:00Z\"");
    }

    [Fact]
    public void Write_UtcKind_SerializesWithZSuffix()
    {
        var value = new DateTime(2026, 7, 4, 22, 1, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(value, Options);

        json.Should().Be("\"2026-07-04T22:01:00Z\"");
    }

    [Fact]
    public void Write_LocalKind_ConvertsToUtc()
    {
        var utcNow = DateTime.UtcNow;
        var local = utcNow.ToLocalTime();

        var json = JsonSerializer.Serialize(local, Options);

        var roundTripped = JsonSerializer.Deserialize<DateTime>(json, Options);
        roundTripped.Should().BeCloseTo(utcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Read_OffsetString_NormalizesToUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-07-04T22:01:00+03:00\"", Options);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 7, 4, 19, 1, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Read_StringWithoutOffset_AssumesUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-07-04T22:01:00\"", Options);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 7, 4, 22, 1, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Nullable_RoundTripsNullAndValue()
    {
        JsonSerializer.Serialize<DateTime?>(null, Options).Should().Be("null");
        JsonSerializer.Deserialize<DateTime?>("null", Options).Should().BeNull();

        var value = new DateTime(2026, 7, 4, 22, 1, 0, DateTimeKind.Unspecified);
        JsonSerializer.Serialize<DateTime?>(value, Options).Should().Be("\"2026-07-04T22:01:00Z\"");
    }
}
