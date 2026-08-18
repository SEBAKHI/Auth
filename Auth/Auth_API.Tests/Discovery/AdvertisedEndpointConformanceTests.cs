using System.Reflection;
using System.Text.Json.Serialization;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth_API.Modules.Authentication.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Discovery;

/// <summary>
/// Guards the promise the discovery document makes about each address it lists.
/// </summary>
/// <remarks>
/// A client library never reads our documentation. It reads
/// <c>/.well-known/openid-configuration</c>, takes each entry as a statement that
/// the address behaves the way its RFC says, and configures itself from that. So
/// an entry that names an endpoint which does not conform is worse than an entry
/// that is absent: absent, the library reports the capability as unsupported and
/// the developer plans around it; present-but-wrong, it tries, fails on an opaque
/// transport error, and the developer looks for the bug in their own code.
/// </remarks>
public class AdvertisedEndpointConformanceTests
{
    private static PropertyInfo Property<T>(string name) =>
        typeof(T).GetProperty(name) ?? throw new InvalidOperationException($"{typeof(T).Name}.{name} is gone");

    private static string? FormName<T>(string property) =>
        Property<T>(property).GetCustomAttribute<FromFormAttribute>()?.Name;

    // --- RFC 7009 revocation, RFC 7662 introspection: form-encoded, snake_case ---

    [Fact]
    public void RevocationRequest_ReadsTheFieldNamesRfc7009Sends()
    {
        FormName<RevokeTokenRequest>(nameof(RevokeTokenRequest.Token)).Should().Be("token");
        FormName<RevokeTokenRequest>(nameof(RevokeTokenRequest.TokenTypeHint)).Should().Be("token_type_hint");
    }

    [Fact]
    public void IntrospectionRequest_ReadsTheFieldNamesRfc7662Sends()
    {
        FormName<IntrospectTokenRequest>(nameof(IntrospectTokenRequest.Token)).Should().Be("token");
        FormName<IntrospectTokenRequest>(nameof(IntrospectTokenRequest.TokenTypeHint)).Should().Be("token_type_hint");
    }

    [Theory]
    [InlineData("access_token", TokenTypeHint.AccessToken)]
    [InlineData("refresh_token", TokenTypeHint.RefreshToken)]
    [InlineData("ACCESS_TOKEN", TokenTypeHint.AccessToken)]
    [InlineData("  refresh_token  ", TokenTypeHint.RefreshToken)]
    public void TokenTypeHint_ParsesTheWireValue(string wire, TokenTypeHint expected)
    {
        // The trap this exists for: the enum carries
        // [JsonStringEnumMemberName("access_token")], but form binding never goes
        // through the JSON converter — it matches the C# member name. Binding the
        // hint straight to the enum would therefore have dropped every conformant
        // value on the floor, silently, and the endpoint would have searched both
        // token types instead of the one it was told.
        new RevokeTokenRequest { TokenTypeHint = wire }.ParsedTokenTypeHint.Should().Be(expected);
        new IntrospectTokenRequest { TokenTypeHint = wire }.ParsedTokenTypeHint.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id_token")]
    [InlineData("nonsense")]
    public void TokenTypeHint_UnknownOrAbsent_IsIgnoredRatherThanRefused(string? wire)
    {
        // Both RFCs make the hint optional and tell the server to fall back to
        // searching every token type. Refusing would be stricter than the standard
        // and would break a client that meant well.
        new RevokeTokenRequest { TokenTypeHint = wire }.ParsedTokenTypeHint.Should().BeNull();
        new IntrospectTokenRequest { TokenTypeHint = wire }.ParsedTokenTypeHint.Should().BeNull();
    }

    // --- OIDC Core 5.3 userinfo ---

    [Fact]
    public void UserInfo_CarriesTheSubjectClaimTheSpecRequires()
    {
        var id = Guid.NewGuid();
        var info = new UserInfo { Id = id, Email = "a@b.c", FirstName = "A", LastName = "B" };

        info.Sub.Should().Be(id.ToString());
        Property<UserInfo>(nameof(UserInfo.Sub))
            .GetCustomAttribute<JsonPropertyNameAttribute>()!.Name.Should().Be("sub");
    }

    [Fact]
    public void UserInfo_SubjectCannotDriftFromTheId()
    {
        // Computed rather than stored, so no code path can set one without the
        // other. A userinfo response whose "sub" names a different user than the
        // token it was fetched with is worse than one with no "sub" at all.
        Property<UserInfo>(nameof(UserInfo.Sub)).CanWrite.Should().BeFalse();
    }
    // --- RFC 7009 2.2: an invalid token is a 200, not an error ---

    [Fact]
    public void RevocationRequest_IsWhatTheFirstPartySdkActuallySends()
    {
        // The regression this guards. Adding [Consumes(form-urlencoded)] to the
        // endpoint answered the shipped C# SDK 415, because it posted JSON. The
        // audit that caught it also caught the claim in the commit message that
        // there were no JSON callers — which had only checked Auth_UI.
        var sdk = File.ReadAllText(SdkClientPath());

        sdk.Should().Contain("FormUrlEncodedContent",
            "the SDK must speak the media type RFC 7662 defines");
        sdk.Should().NotContain("PostAsJsonAsync(" + Environment.NewLine + "                \"/api/v1/auth/introspect\"",
            "posting JSON to introspection is answered 415");
    }

    private static string SdkClientPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Auth.Sdk")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the Auth.Sdk project must be locatable from the test output");
        return Path.Combine(dir!.FullName, "Auth.Sdk", "AuthSystemClient.cs");
    }
}
