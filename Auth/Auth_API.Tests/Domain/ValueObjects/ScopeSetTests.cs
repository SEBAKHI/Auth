using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// The OAuth scope vocabulary and its one parser (OI-58, B1–B3): what an authorize
/// request may ask for, how it is written down, and the grant rule
/// <c>{openid} ∪ (requested ∩ allowed)</c>.
/// </summary>
public class ScopeSetTests
{
    /// <summary>A raw <c>scope</c> parameter and the canonical set it parses to.</summary>
    public static TheoryData<string?, string> ParsedValues => new()
    {
        // No parameter is openid alone (D-58-1).
        { null, "openid" },
        { "", "openid" },
        { "openid", "openid" },
        { "openid profile", "openid profile" },
        // openid is in every set, asked for or not.
        { "profile", "openid profile" },
        // Repeated, leading and trailing spaces are tolerated.
        { "  openid   email  ", "openid email" },
        { "   ", "openid" },
        // Duplicates collapse, and the order is canonical whatever the request's.
        { "phone phone email openid profile email", "openid profile email phone" },
        // offline_access is recognised and adds nothing (D-58-2).
        { "offline_access", "openid" },
        { "openid offline_access phone", "openid phone" },
    };

    /// <summary>A well-formed value naming a scope this server does not know.</summary>
    public static TheoryData<string> UnknownValues => new()
    {
        // Case-sensitive: OpenID is not openid (RFC 6749 §3.3).
        "OpenID",
        "openid Profile",
        // A claim name sent by mistake for its scope.
        "phone_number",
        "openid address",
    };

    /// <summary>A value outside the scope-token grammar, or too long.</summary>
    public static TheoryData<string> MalformedValues => new()
    {
        "openid\tprofile",
        "openid \"profile\"",
        "openid pro\\file",
        "openidé",
        "openid\nprofile",
        new string('a', ScopeSet.MaxLength + 1),
        // Over the limit even though every word is known and the spaces are legal.
        "openid" + new string(' ', ScopeSet.MaxLength),
    };

    [Fact]
    public void TheTheories_HaveCases()
    {
        // A theory with no rows passes vacuously; pin that each one runs something.
        ParsedValues.Count<object[]>().Should().BeGreaterThan(0);
        UnknownValues.Count<object[]>().Should().BeGreaterThan(0);
        MalformedValues.Count<object[]>().Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(ParsedValues))]
    public void TryParse_KnownScopes_ReturnsTheCanonicalSet(string? value, string expected)
    {
        var status = ScopeSet.TryParse(value, out var scopes);

        status.Should().Be(ScopeSet.ParseStatus.Parsed);
        scopes.Value.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(UnknownValues))]
    public void TryParse_UnknownScope_ReturnsUnknown(string value)
    {
        ScopeSet.TryParse(value, out var scopes).Should().Be(ScopeSet.ParseStatus.Unknown);
        scopes.Should().Be(ScopeSet.OpenIdOnly);
    }

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public void TryParse_MalformedValue_ReturnsMalformed(string value)
    {
        ScopeSet.TryParse(value, out _).Should().Be(ScopeSet.ParseStatus.Malformed);
    }

    [Fact]
    public void TryParse_AtTheLengthLimit_IsStillParsed()
    {
        var value = "openid" + new string(' ', ScopeSet.MaxLength - "openid".Length);

        ScopeSet.TryParse(value, out var scopes).Should().Be(ScopeSet.ParseStatus.Parsed);
        scopes.Value.Should().Be("openid");
    }

    [Fact]
    public void Intersect_RequestedAndAllowed_IsTheGrantWithOpenIdAlwaysIn()
    {
        ScopeSet.TryParse("openid profile email phone", out var requested);
        var allowed = ScopeSet.FromStored("profile email");

        requested.Intersect(allowed).Value.Should().Be("openid profile email");
    }

    [Fact]
    public void Intersect_NothingAllowed_IsOpenIdOnly()
    {
        ScopeSet.TryParse("phone", out var requested);

        requested.Intersect(ScopeSet.FromStored(null)).Should().Be(ScopeSet.OpenIdOnly);
    }

    [Fact]
    public void Except_ListsTheRequestedScopesTheAllowedSetDrops()
    {
        ScopeSet.TryParse("openid profile phone", out var requested);

        requested.Except(ScopeSet.FromStored("profile")).Should().Equal("phone");
        requested.Except(requested).Should().BeEmpty();
    }

    [Fact]
    public void FromStored_Null_IsOpenIdOnly_AndUnknownNamesAreDropped()
    {
        ScopeSet.FromStored(null).Should().Be(ScopeSet.OpenIdOnly);
        ScopeSet.FromStored("").Should().Be(ScopeSet.OpenIdOnly);
        // A stored row is trusted text: a name the server no longer knows is
        // dropped instead of failing a sign-in.
        ScopeSet.FromStored("phone retired_scope email").Value.Should().Be("openid email phone");
    }

    [Fact]
    public void FromNames_KnownNames_IsCanonical_AndOpenIdIsNormalizedAway()
    {
        var set = ScopeSet.FromNames(["phone", "email", "phone"]);

        set.IsError.Should().BeFalse();
        set.Value.Value.Should().Be("openid email phone");
        set.Value.OptionalValue.Should().Be("email phone");
        set.Value.OptionalNames.Should().Equal("email", "phone");

        var openIdOnly = ScopeSet.FromNames(["openid"]);
        openIdOnly.Value.OptionalValue.Should().BeNull();
        openIdOnly.Value.OptionalNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("address")]
    [InlineData("Phone")]
    [InlineData("offline_access")]
    [InlineData(null)]
    public void FromNames_UnknownName_ReturnsAllowedScopesInvalid(string? name)
    {
        var set = ScopeSet.FromNames(["email", name]);

        set.IsError.Should().BeTrue();
        set.FirstError.Code.Should().Be(ApplicationErrors.AllowedScopesInvalid.Code);
    }

    [Fact]
    public void Equality_IsByCanonicalValue()
    {
        ScopeSet.FromStored("email profile").Should().Be(ScopeSet.FromStored("openid profile email"));
        ScopeSet.FromStored("email").Should().NotBe(ScopeSet.FromStored("phone"));
        ScopeSet.FromStored("email").GetHashCode().Should().Be(ScopeSet.FromStored("openid email").GetHashCode());
    }
}
