using System.Reflection;
using System.Text.Json.Serialization;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth_API.Modules.Authentication.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Auth_API.Modules.Authentication.Controllers;
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
    // --- OIDC RP-Initiated Logout: end_session_endpoint ---

    [Fact]
    public void EndSession_IsAdvertisedAtAnAddressABrowserCanNavigateTo()
    {
        // The entry used to name /auth/logout, which is POST + [Authorize]. A
        // relying party following the spec navigates the user agent to this
        // address, so it got 405 for the GET, or 401 for a POST navigation that
        // carries no bearer header. The document promised an endpoint that could
        // not be reached the only way the standard reaches it.
        var handler = typeof(Auth_API.Controllers.DiscoveryController);
        handler.Should().NotBeNull();

        var endSession = typeof(AuthController).GetMethod(nameof(AuthController.EndSession));
        endSession.Should().NotBeNull("end_session_endpoint must resolve to a real action");

        endSession!.GetCustomAttributes<HttpGetAttribute>().Should().ContainSingle()
            .Which.Template.Should().Be("end-session");
        endSession.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty(
            "a browser navigation from another site carries no bearer token");
        endSession.GetCustomAttributes<AllowAnonymousAttribute>().Should().NotBeEmpty();
    }

    [Fact]
    public void EndSessionConfirmation_IsAnonymousAndRateLimited()
    {
        // The cookie is the credential here, not a bearer token. SameSite=Lax is
        // what stops a cross-site page forging this POST — Lax withholds the
        // cookie from cross-site POSTs, so a forged call arrives with nothing to
        // act on. The rate limit covers the rest.
        var confirm = typeof(AuthController).GetMethod(nameof(AuthController.ConfirmEndSession));
        confirm.Should().NotBeNull();

        confirm!.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle()
            .Which.Template.Should().Be("end-session");
        confirm.GetCustomAttributes<AllowAnonymousAttribute>().Should().NotBeEmpty();
        confirm.GetCustomAttributes<EnableRateLimitingAttribute>().Should().NotBeEmpty();
    }
}
