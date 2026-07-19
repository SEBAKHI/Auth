using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Application.Configuration;
using Auth.Application.Features.Discovery.GetDiscoveryDocument;
using Microsoft.Extensions.Options;

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
            Options.Create(new JwtSettings { Issuer = Issuer }));
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
        document.UserinfoEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/me");
        document.EndSessionEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/logout");
        document.RevocationEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/revoke");
        document.IntrospectionEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/introspect");
    }

    [Fact]
    public async Task Handle_OnlyAdvertisesImplementedCapabilities()
    {
        // Act
        var result = await _handler.Handle(new GetDiscoveryDocumentQuery(BaseUrl), CancellationToken.None);

        // Assert — the authorization-code + PKCE flow exists; OIDC id_tokens
        // and scopes still do not, so they stay unadvertised.
        result.IsError.Should().BeFalse();
        var document = result.Value;

        document.ResponseTypesSupported.Should().BeEquivalentTo("code");
        document.CodeChallengeMethodsSupported.Should().BeEquivalentTo("S256");
        document.GrantTypesSupported.Should().BeEquivalentTo("authorization_code", "refresh_token");
        document.TokenEndpointAuthMethodsSupported.Should().BeEquivalentTo("none");
        document.SubjectTypesSupported.Should().BeEquivalentTo("public");

        document.ScopesSupported.Should().BeNull();
        document.IdTokenSigningAlgValuesSupported.Should().BeNull();
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

        // Unimplemented capabilities must be absent from the wire format entirely.
        json.Should().NotContain("scopes_supported");
        json.Should().NotContain("id_token_signing_alg_values_supported");
    }
}
