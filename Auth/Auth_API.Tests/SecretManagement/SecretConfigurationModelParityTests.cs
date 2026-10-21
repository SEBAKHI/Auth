using System.Reflection;
using ApplicationModel = Auth.Application.Configuration.SecretConfiguration;
using SharedModel = Auth.Shared.Configuration.SecretConfiguration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 TS7 (KR1, P4-R1): two classes deserialize the one secrets file — the API's
/// service reads and writes it with the Application model, the configuration
/// provider reads it with the Shared one. Neither keeps unknown members, so a field
/// that exists in one model only is erased by the first save through the other.
/// They must be the same shape, nested types included.
/// </summary>
public class SecretConfigurationModelParityTests
{
    [Fact]
    public void TheTwoModels_HaveTheSameMembers_WithTheSameTypes()
    {
        Shape(typeof(ApplicationModel)).Should().Equal(Shape(typeof(SharedModel)));
    }

    [Theory]
    [InlineData("HmacKeys")]
    [InlineData("HmacCurrentKeyId")]
    [InlineData("HmacNextKeyId")]
    [InlineData("HmacLegacyCutoverUtc")]
    [InlineData("HmacLegacyVerifyUntilUtc")]
    // Empty until S12, present now so a build without S12 does not erase an RSA ring.
    [InlineData("SigningKeys")]
    [InlineData("SigningCurrentKid")]
    [InlineData("SigningNextKid")]
    public void BothModels_CarryTheKeyrings(string member)
    {
        typeof(ApplicationModel).GetProperty(member).Should().NotBeNull();
        typeof(SharedModel).GetProperty(member).Should().NotBeNull();
    }

    [Fact]
    public void TheCollections_StartEmpty()
    {
        // A collection initialized with content would be appended to by the binder, not
        // replaced (project_config_array_binder_append); empty also means "no ring".
        new ApplicationModel().HmacKeys.Should().BeEmpty();
        new ApplicationModel().SigningKeys.Should().BeEmpty();
        new SharedModel().HmacKeys.Should().BeEmpty();
        new SharedModel().SigningKeys.Should().BeEmpty();
    }

    /// <summary>
    /// Every serialized member as "Name: Type", nested model types written out as their
    /// own members, so the two namespaces compare equal only when the shapes do.
    /// </summary>
    private static List<string> Shape(Type type, string prefix = "")
    {
        var lines = new List<string>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            lines.Add($"{prefix}{property.Name}: {Describe(property.PropertyType)}");
            foreach (var nested in ModelTypes(property.PropertyType))
            {
                lines.AddRange(Shape(nested, $"{prefix}{property.Name}.{nested.Name}."));
            }
        }

        return lines;
    }

    private static bool IsModelType(Type type) =>
        type.Namespace is "Auth.Application.Configuration" or "Auth.Shared.Configuration" && type.IsClass;

    private static IEnumerable<Type> ModelTypes(Type type) =>
        IsModelType(type)
            ? [type]
            : type.IsGenericType ? type.GetGenericArguments().SelectMany(ModelTypes) : [];

    private static string Describe(Type type)
    {
        if (IsModelType(type))
        {
            return "model:" + type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Describe(underlying) + "?";
        }

        return type.IsGenericType
            ? $"{type.GetGenericTypeDefinition().Name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
            : type.FullName!;
    }
}
