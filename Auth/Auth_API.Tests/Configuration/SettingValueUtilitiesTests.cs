using System.Text.Json;
using Auth.Application.SystemSettings;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.Configuration;

#region SettingValueReader Tests

public class SettingValueReaderTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public void ReadTyped_Bool_ParsesToBoolean()
    {
        var configuration = Config(("Feature:Enabled", "true"));

        SettingValueReader.ReadTyped(configuration, SettingKind.Bool, "Feature:Enabled")
            .Should().Be(true);
    }

    [Fact]
    public void ReadTyped_BoolUnparseable_FallsBackToRawString()
    {
        var configuration = Config(("Feature:Enabled", "yes"));

        SettingValueReader.ReadTyped(configuration, SettingKind.Bool, "Feature:Enabled")
            .Should().Be("yes");
    }

    [Fact]
    public void ReadTyped_Int_ParsesToLong()
    {
        var configuration = Config(("Password:MinimumLength", "42"));

        SettingValueReader.ReadTyped(configuration, SettingKind.Int, "Password:MinimumLength")
            .Should().Be(42L);
    }

    [Fact]
    public void ReadTyped_IntUnparseable_FallsBackToRawString()
    {
        var configuration = Config(("Password:MinimumLength", "many"));

        SettingValueReader.ReadTyped(configuration, SettingKind.Int, "Password:MinimumLength")
            .Should().Be("many");
    }

    [Fact]
    public void ReadTyped_StringAndEnum_ReturnRawValue()
    {
        var configuration = Config(
            ("Jwt:Issuer", "https://auth.example.com"),
            ("Password:BreachedPasswordCheck:Mode", "Warn"));

        SettingValueReader.ReadTyped(configuration, SettingKind.String, "Jwt:Issuer")
            .Should().Be("https://auth.example.com");
        SettingValueReader.ReadTyped(configuration, SettingKind.Enum, "Password:BreachedPasswordCheck:Mode")
            .Should().Be("Warn");
    }

    [Theory]
    [InlineData(SettingKind.Bool)]
    [InlineData(SettingKind.Int)]
    [InlineData(SettingKind.String)]
    [InlineData(SettingKind.Enum)]
    [InlineData(SettingKind.StringArray)]
    public void ReadTyped_MissingKey_ReturnsNull(SettingKind kind)
    {
        var configuration = Config();

        SettingValueReader.ReadTyped(configuration, kind, "Absent:Key").Should().BeNull();
    }

    [Fact]
    public void ReadTyped_StringArray_DropsEmptyTombstoneEntries()
    {
        // The database layer masks residual file elements with empty-string
        // tombstones; readers must never surface them.
        var configuration = Config(
            ("Gateway:ExemptPaths:0", "/health"),
            ("Gateway:ExemptPaths:1", ""),
            ("Gateway:ExemptPaths:2", "/ready"));

        var value = SettingValueReader.ReadTyped(configuration, SettingKind.StringArray, "Gateway:ExemptPaths");

        value.Should().BeOfType<object[]>().Which.Should().Equal("/health", "/ready");
    }

    [Fact]
    public void ReadTyped_StringArrayAllTombstones_ReturnsNull()
    {
        var configuration = Config(
            ("Gateway:ExemptPaths:0", ""),
            ("Gateway:ExemptPaths:1", ""));

        SettingValueReader.ReadTyped(configuration, SettingKind.StringArray, "Gateway:ExemptPaths")
            .Should().BeNull();
    }

    [Fact]
    public void ReadCanonical_Scalar_ReturnsRawValueAndNullWhenUnset()
    {
        var configuration = Config(("Jwt:Issuer", "https://auth.example.com"));

        SettingValueReader.ReadCanonical(configuration, SettingKind.String, "Jwt:Issuer")
            .Should().Be("https://auth.example.com");
        SettingValueReader.ReadCanonical(configuration, SettingKind.String, "Absent:Key")
            .Should().BeNull();
    }

    [Fact]
    public void ReadCanonical_Array_JoinsElementsWithUnitSeparatorInIndexOrder()
    {
        var configuration = Config(
            ("Gateway:ExemptPaths:0", "/health"),
            ("Gateway:ExemptPaths:1", "/ready"));

        SettingValueReader.ReadCanonical(configuration, SettingKind.StringArray, "Gateway:ExemptPaths")
            .Should().Be("/health" + SettingValueReader.ArraySeparator + "/ready");
    }

    [Fact]
    public void ReadCanonical_Array_TombstonedAndPlainStatesCompareEqual()
    {
        // Restart-pending detection compares canonical strings across two
        // configuration states; tombstones must not create false diffs.
        var plain = Config(
            ("Gateway:ExemptPaths:0", "/health"),
            ("Gateway:ExemptPaths:1", "/ready"));
        var tombstoned = Config(
            ("Gateway:ExemptPaths:0", "/health"),
            ("Gateway:ExemptPaths:1", ""),
            ("Gateway:ExemptPaths:2", "/ready"));

        var left = SettingValueReader.ReadCanonical(plain, SettingKind.StringArray, "Gateway:ExemptPaths");
        var right = SettingValueReader.ReadCanonical(tombstoned, SettingKind.StringArray, "Gateway:ExemptPaths");

        left.Should().Be(right);
    }

    [Fact]
    public void ReadCanonical_EmptyArray_ReturnsNull()
    {
        var configuration = Config(("Gateway:ExemptPaths:0", ""));

        SettingValueReader.ReadCanonical(configuration, SettingKind.StringArray, "Gateway:ExemptPaths")
            .Should().BeNull();
    }
}

#endregion

#region JsonOverrideFlattener Tests

public class JsonOverrideFlattenerTests
{
    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Flatten_NestedObject_ProducesColonSeparatedPaths()
    {
        var result = JsonOverrideFlattener.Flatten(
            Json("""{"A":{"B":{"C":1}},"D":"x"}"""), expandArrays: false);

        result.Select(p => p.Key).Should().Equal("A:B:C", "D");
        result[0].Value.GetInt32().Should().Be(1);
        result[1].Value.GetString().Should().Be("x");
    }

    [Fact]
    public void Flatten_ExpandArraysFalse_KeepsArraysWhole()
    {
        var result = JsonOverrideFlattener.Flatten(
            Json("""{"ExemptPaths":["/health","/ready"]}"""), expandArrays: false);

        result.Should().ContainSingle();
        result[0].Key.Should().Be("ExemptPaths");
        result[0].Value.ValueKind.Should().Be(JsonValueKind.Array);
        result[0].Value.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Flatten_ExpandArraysTrue_ExpandsElementsByIndex()
    {
        var result = JsonOverrideFlattener.Flatten(
            Json("""{"ExemptPaths":["/health","/ready"]}"""), expandArrays: true);

        result.Select(p => (p.Key, p.Value.GetString()))
            .Should().Equal(("ExemptPaths:0", "/health"), ("ExemptPaths:1", "/ready"));
    }

    [Fact]
    public void Flatten_ExpandArraysTrue_RecursesIntoObjectsInsideArrays()
    {
        var result = JsonOverrideFlattener.Flatten(
            Json("""{"Items":[{"X":1},{"X":2}]}"""), expandArrays: true);

        result.Select(p => p.Key).Should().Equal("Items:0:X", "Items:1:X");
    }

    [Fact]
    public void Flatten_ArrayNestedInsideObject_StaysWholeWhenNotExpanding()
    {
        var result = JsonOverrideFlattener.Flatten(
            Json("""{"Outer":{"Arr":["a"]}}"""), expandArrays: false);

        result.Should().ContainSingle();
        result[0].Key.Should().Be("Outer:Arr");
        result[0].Value.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void Flatten_NullLeaf_IsCapturedAtItsPath()
    {
        var result = JsonOverrideFlattener.Flatten(Json("""{"N":null}"""), expandArrays: false);

        result.Should().ContainSingle();
        result[0].Key.Should().Be("N");
        result[0].Value.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Flatten_EmptyObject_YieldsNothing()
    {
        JsonOverrideFlattener.Flatten(Json("{}"), expandArrays: false).Should().BeEmpty();
        JsonOverrideFlattener.Flatten(Json("{}"), expandArrays: true).Should().BeEmpty();
    }
}

#endregion
