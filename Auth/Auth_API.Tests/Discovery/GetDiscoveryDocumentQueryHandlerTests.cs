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
        document.TokenEndpoint.Should().Be($"{BaseUrl}/api/v1/auth/login");
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

        // Assert — no authorization-code flow exists yet, so nothing that depends
        // on an authorization endpoint may be advertised (Phase 4 re-adds them).
        result.IsError.Should().BeFalse();
        var document = result.Value;

        document.AuthorizationEndpoint.Should().BeNull();
        document.ResponseTypesSupported.Should().BeEmpty();
        document.CodeChallengeMethodsSupported.Should().BeNull();
        document.ScopesSupported.Should().BeNull();
        document.IdTokenSigningAlgValuesSupported.Should().BeNull();

        document.GrantTypesSupported.Should().BeEquivalentTo("password", "refresh_token");
        document.TokenEndpointAuthMethodsSupported.Should().BeEquivalentTo("none");
        document.SubjectTypesSupported.Should().BeEquivalentTo("public");
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
        json.Should().Contain("\"token_endpoint\"");
        json.Should().Contain("\"grant_types_supported\"");
        json.Should().NotContain("jwksUri");
        json.Should().NotContain("tokenEndpoint");

        // Unimplemented capabilities must be absent from the wire format entirely.
        json.Should().NotContain("authorization_endpoint");
        json.Should().NotContain("code_challenge_methods_supported");
        json.Should().NotContain("scopes_supported");
        json.Should().NotContain("id_token_signing_alg_values_supported");
    }
}
