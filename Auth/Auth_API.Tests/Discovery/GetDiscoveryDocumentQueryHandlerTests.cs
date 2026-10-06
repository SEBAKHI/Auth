using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Application.Configuration;
using Auth.Application.Features.Discovery.GetDiscoveryDocument;
using Microsoft.Extensions.Options;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Discovery;

/// <summary>
/// Unit tests for GetDiscoveryDocumentQueryHandler.
/// </summary>
public class GetDiscoveryDocumentQueryHandlerTests
{
    private const string BaseUrl = "https://auth.example.com";
    private const string Issuer = "https://auth.example.com";

    private readonly GetDiscoveryDocumentQueryHandler _handler;

    public GetDiscoveryDocumentQueryHandlerTests()
    {
        _handler = new GetDiscoveryDocumentQueryHandler(
            TestHelpers.CreateOptions(new JwtSettings { Issuer = Issuer }));
    }

    [Fact]
    public async Task Handle_ReturnsEndpointsBuiltFromBaseUrl()
    {
        // Act
        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var document = result.Value;

        document.Issuer.Should().Be(Issuer);
        document.JwksUri.Should().Be($"{BaseUrl}/.well-known/jwks.json");
        document.AuthorizationEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/authorize");
        document.TokenEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/token");
        // The OIDC UserInfo endpoint, which takes an application's token; /auth/me refuses one.
        document.UserinfoEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/userinfo");
        // Not /auth/logout: that one is POST + bearer, which the browser
        // navigation this endpoint is defined as cannot satisfy.
        document.EndSessionEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/end-session");
        document.RevocationEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/revoke");
        // Introspection needs a platform token, so no application could call it.
        document.IntrospectionEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OnlyAdvertisesImplementedCapabilities()
    {
        // Act
        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        // Assert — the authorization-code + PKCE flow and its scopes exist;
        // OIDC id_tokens still do not, so they stay unadvertised.
        result.IsError.Should().BeFalse();
        var document = result.Value;

        document.ResponseTypesSupported.Should().BeEquivalentTo("code");
        document.CodeChallengeMethodsSupported.Should().BeEquivalentTo("S256");
        document.GrantTypesSupported.Should().BeEquivalentTo("authorization_code", "refresh_token");
        document.TokenEndpointAuthMethodsSupported.Should().BeEquivalentTo("none");
        document.SubjectTypesSupported.Should().BeEquivalentTo("public");

        // Exactly the scopes /auth/authorize grants, in canonical order; nothing
        // it would refuse with invalid_scope (offline_access is accepted there
        // but never granted, so it is not advertised).
        document.ScopesSupported.Should().Equal("openid", "profile", "email", "phone");
        document.IdTokenSigningAlgValuesSupported.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClaimsSupported_AddsWhatUserInfoReturns()
    {
        // The access token's nine claims, unchanged, then the eight UserInfo can add for the
        // granted scopes.
        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        result.Value.ClaimsSupported.Should().Equal(
            "sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss",
            "given_name", "family_name", "locale", "zoneinfo", "picture", "email_verified",
            "phone_number", "phone_number_verified");
    }

    [Fact]
    public async Task Serialization_UsesOidcMetadataNames_DespiteGlobalCamelCasePolicy()
    {
        // Arrange — mirror the API's global JSON options (Program.cs): standard OIDC
        // consumers such as Auth.Sdk's JwtBearer metadata retriever only recognize
        // the snake_case names, so the attribute names must win over the policy.
        var apiJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        // Act
        var json = JsonSerializer.Serialize(result.Value, apiJsonOptions);

        // Assert
        json.Should().Contain("\"issuer\"");
        json.Should().Contain("\"jwks_uri\"");
        json.Should().Contain("\"authorization_endpoint\"");
        json.Should().Contain("\"token_endpoint\"");
        json.Should().Contain("\"grant_types_supported\"");
        json.Should().Contain("\"code_challenge_methods_supported\"");
        json.Should().NotContain("jwksUri");
        json.Should().NotContain("tokenEndpoint");

        // Scopes are implemented, so they are on the wire under their OIDC name;
        // unimplemented capabilities must be absent from the wire format entirely.
        json.Should().Contain("\"scopes_supported\":[\"openid\",\"profile\",\"email\",\"phone\"]");
        json.Should().NotContain("scopesSupported");
        json.Should().NotContain("id_token_signing_alg_values_supported");
    }

    [Fact]
    public async Task Handle_AdvertisesPromptCreate()
    {
        // OIDC Initiating User Registration 1.0: a relying party learns from
        // discovery that prompt=create opens registration (OI-63).
        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        result.Value.PromptValuesSupported.Should().BeEquivalentTo("login", "none", "create");
    }

    [Fact]
    public async Task Serialization_HasNoIntrospectionEndpoint()
    {
        // The key itself must be gone, because a client library treats a listed endpoint as one
        // it may call. Serialized WITHOUT the API's global null-dropping, so the absence cannot
        // rest on a setting in Program.cs that someone may change.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, options));

        document.RootElement.TryGetProperty("introspection_endpoint", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("introspectionEndpoint", out _).Should().BeFalse();
        document.RootElement.GetProperty("userinfo_endpoint").GetString()
            .Should().Be($"{BaseUrl}/api/v1/auth/userinfo");
    }
}
