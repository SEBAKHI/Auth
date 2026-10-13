using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ClientCredentials;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// The client_credentials grant end to end inside the process (OI-128, T1 to T3): a real assertion,
/// the real verifier, the real token service with a throwaway RSA key; only the two stores are
/// mocks, each Setup and Verify passing It.IsAny for every parameter.
/// </summary>
public sealed class ClientCredentialsCommandHandlerTests : IDisposable
{
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly Guid KeyId = Guid.NewGuid();

    private readonly MachineCredentials _credentials = new();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();
    private readonly JwtSettings _settings = new()
    {
        Issuer = MachineCredentials.Issuer,
        Audience = "authsystem-api",
        KeyId = "test-key"
    };
    private readonly JwtTokenService _tokens;

    public ClientCredentialsCommandHandlerTests()
    {
        _tokens = new JwtTokenService(Options.Create(_settings), Mock.Of<IPasswordHasher>());

        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => kid == _credentials.Kid ? Credential() : null);
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose()
    {
        _tokens.Dispose();
        _credentials.Dispose();
    }

    private MachineKeyCredential Credential() => new(
        new ApplicationMachineKey(
            KeyId, ApplicationId, _credentials.Kid, "server", MachineClients.SigningAlgorithm,
            _credentials.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null),
        MachineCredentials.ClientId);

    private ClientCredentialsCommandHandler Handler() => new(
        _keys.Object,
        _jtis.Object,
        new ClientAssertionVerifier(TimeProvider.System),
        _tokens,
        TestHelpers.CreateOptions(_settings),
        NullLogger<ClientCredentialsCommandHandler>.Instance);

    private static ClientCredentialsCommand Command(
        string? assertion,
        string? clientId = null,
        string? scope = null,
        string? assertionType = ClientCredentialsCommandHandler.JwtBearerAssertionType) =>
        new(assertionType, assertion, clientId, scope, "203.0.113.7");

    private Task<ErrorOr<ClientCredentialsTokenResponse>> SendAsync(ClientCredentialsCommand command) =>
        Handler().Handle(command, CancellationToken.None);

    private static void ShouldBeInvalidClient(ErrorOr<ClientCredentialsTokenResponse> result)
    {
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
        result.FirstError.Type.Should().Be(ErrorType.Validation);
    }

    private void NothingWasIssued()
    {
        _jtis.Verify(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _keys.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- T1: the token ---

    [Fact]
    public async Task Handle_AValidAssertion_IssuesExactlyTheMachineToken()
    {
        var result = await SendAsync(Command(_credentials.Assertion(), clientId: MachineCredentials.ClientId));

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        var response = result.Value;
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(300);
        response.Scope.Should().Be("apikeys.validate webhookkeys.validate tokens.introspect");

        var token = new JsonWebToken(response.AccessToken);
        token.Alg.Should().Be("RS256");
        token.Typ.Should().Be("at+jwt");
        token.Kid.Should().Be("test-key");

        using var payload = JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(response.AccessToken.Split('.')[1]));
        payload.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("iss", "sub", "client_id", "aud", "scope", "jti", "iat", "exp");
        payload.RootElement.GetProperty("iss").GetString().Should().Be(MachineCredentials.Issuer);
        payload.RootElement.GetProperty("sub").GetString().Should().Be(MachineCredentials.ClientId);
        payload.RootElement.GetProperty("client_id").GetString().Should().Be(MachineCredentials.ClientId);
        payload.RootElement.GetProperty("aud").ValueKind.Should().Be(JsonValueKind.String, "one audience, as a string");
        payload.RootElement.GetProperty("aud").GetString().Should().Be("urn:authsystem:machine");
        payload.RootElement.GetProperty("scope").ValueKind.Should().Be(JsonValueKind.String, "RFC 9068: one space-delimited string");
        (payload.RootElement.GetProperty("exp").GetInt64() - payload.RootElement.GetProperty("iat").GetInt64()).Should().Be(300);

        // And the machine scheme, built from the same settings and key, accepts it.
        var validation = await new JsonWebTokenHandler { MapInboundClaims = false }
            .ValidateTokenAsync(response.AccessToken, AccessTokenValidation.Machine(_settings, _tokens.GetSecurityKey()));
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
    }

    [Fact]
    public async Task Handle_AValidAssertion_AnswersWithoutARefreshOrIdToken()
    {
        var result = await SendAsync(Command(_credentials.Assertion()));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("access_token", "token_type", "expires_in", "scope");
    }

    [Fact]
    public async Task Handle_AValidAssertion_ConsumesItsJtiThenRecordsTheKeyUse()
    {
        string? jti = null;
        var assertion = _credentials.Assertion(d => jti = (string)d.Claims["jti"]);

        var result = await SendAsync(Command(assertion));

        result.IsError.Should().BeFalse();
        _jtis.Verify(s => s.TryConsumeAsync(ApplicationId, jti!, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _keys.Verify(r => r.RecordUsageAsync(KeyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("apikeys.validate", "apikeys.validate")]
    [InlineData("tokens.introspect apikeys.validate", "apikeys.validate tokens.introspect")]
    [InlineData("  webhookkeys.validate  ", "webhookkeys.validate")]
    [InlineData("", "apikeys.validate webhookkeys.validate tokens.introspect")]
    public async Task Handle_RequestedScopes_AreGrantedInCatalogueOrder(string requested, string granted)
    {
        var result = await SendAsync(Command(_credentials.Assertion(), scope: requested));

        result.IsError.Should().BeFalse();
        result.Value.Scope.Should().Be(granted);
        new JsonWebToken(result.Value.AccessToken).GetPayloadValue<string>("scope").Should().Be(granted);
    }

    [Theory]
    [InlineData("openid")]
    [InlineData("apikeys:validate")]
    [InlineData("apikeys.validate users:read")]
    public async Task Handle_AnUnknownScope_IsInvalidScope_AndIssuesNothing(string scope)
    {
        var result = await SendAsync(Command(_credentials.Assertion(), scope: scope));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidScope.Code);
        NothingWasIssued();
    }

    // --- T2: the refusals decided before or around the verifier ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("urn:ietf:params:oauth:client-assertion-type:saml2-bearer")]
    [InlineData("URN:IETF:PARAMS:OAUTH:CLIENT-ASSERTION-TYPE:JWT-BEARER")]
    public async Task Handle_NotTheJwtBearerAssertionType_IsInvalidClient(string? assertionType)
    {
        var result = await SendAsync(Command(_credentials.Assertion(), assertionType: assertionType));

        ShouldBeInvalidClient(result);
        NothingWasIssued();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task Handle_NoReadableAssertion_IsInvalidClient(string? assertion)
    {
        ShouldBeInvalidClient(await SendAsync(Command(assertion)));
        _keys.Verify(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// An unknown kid, and the three cases the store answers the same way: a revoked key, a key of
    /// an inactive application, a key of a deleted application (its query joins the live
    /// application and the unrevoked key, ApplicationMachineKeySqlTests).
    /// </summary>
    [Fact]
    public async Task Handle_NoActiveKeyOfALiveApplicationForTheKid_IsInvalidClient()
    {
        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MachineKeyCredential?)null);

        ShouldBeInvalidClient(await SendAsync(Command(_credentials.Assertion())));
        NothingWasIssued();
    }

    [Fact]
    public async Task Handle_AGuidShapedApplicationCode_IsInvalidClient_AndNothingIsIssued()
    {
        // F1: a GUID-shaped code would become a GUID-shaped sub, which several readers take for a
        // user id. The assertion is otherwise perfect for that code, so only the shape refuses it.
        var guidCode = Guid.NewGuid().ToString();
        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MachineKeyCredential(Credential().Key, guidCode));
        var assertion = _credentials.Assertion(d =>
        {
            d.Issuer = guidCode;
            d.Claims["sub"] = guidCode;
        });

        ShouldBeInvalidClient(await SendAsync(Command(assertion, clientId: guidCode)));
        NothingWasIssued();
    }

    [Fact]
    public async Task Handle_AFormClientIdThatIsNotTheKeysApplication_IsInvalidClient()
    {
        ShouldBeInvalidClient(await SendAsync(Command(_credentials.Assertion(), clientId: "other-app")));
        NothingWasIssued();
    }

    [Fact]
    public async Task Handle_AnAssertionNamingAnotherClient_IsInvalidClient()
    {
        // sub and iss name "other-app", the kid names an "shop" key.
        var assertion = _credentials.Assertion(d =>
        {
            d.Issuer = "other-app";
            d.Claims["sub"] = "other-app";
        });

        ShouldBeInvalidClient(await SendAsync(Command(assertion)));
        NothingWasIssued();
    }

    [Fact]
    public async Task Handle_TheTokenEndpointAsAudience_IsInvalidClient_AndItsJtiIsNotSpent()
    {
        // Every verifier refusal is the same answer, and a refused assertion never reaches the jti
        // table: an unverified jti written there would let anyone pre-spend an application's next ones.
        var assertion = _credentials.Assertion(d => d.Audience = MachineCredentials.TokenEndpoint);

        ShouldBeInvalidClient(await SendAsync(Command(assertion)));
        NothingWasIssued();
    }

    [Fact]
    public async Task Handle_EveryRefusal_IsTheSameAnswer()
    {
        // No oracle: the code and the description are the same whichever check failed.
        var refusals = new[]
        {
            await SendAsync(Command(_credentials.Assertion(), assertionType: "x")),
            await SendAsync(Command("not-a-jwt")),
            await SendAsync(Command(_credentials.Assertion(), clientId: "other-app")),
            await SendAsync(Command(_credentials.Assertion(d => d.Audience = MachineCredentials.TokenEndpoint))),
            await SendAsync(Command(_credentials.Assertion(d => d.Expires = DateTime.UtcNow.AddMinutes(10)))),
        };

        refusals.Select(r => (r.FirstError.Code, r.FirstError.Description, r.FirstError.Type)).Distinct()
            .Should().ContainSingle();
    }

    // --- T3: replay ---

    [Fact]
    public async Task Handle_AReplayedJti_IsInvalidClient_AndIssuesNoToken()
    {
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ShouldBeInvalidClient(await SendAsync(Command(_credentials.Assertion())));
        _keys.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TheSameAssertionTwice_SecondIsRefused()
    {
        // The store as the database behaves: a jti is recorded once per application.
        var seen = new HashSet<(Guid, string)>();
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid app, string jti, DateTime _, CancellationToken _) => seen.Add((app, jti)));
        var assertion = _credentials.Assertion();

        var first = await SendAsync(Command(assertion));
        var second = await SendAsync(Command(assertion));

        first.IsError.Should().BeFalse();
        ShouldBeInvalidClient(second);
    }

    [Fact]
    public async Task Handle_TheRequestToken_ReachesEveryStoreCall()
    {
        using var source = new CancellationTokenSource();
        var token = source.Token;

        var result = await Handler().Handle(Command(_credentials.Assertion()), token);

        result.IsError.Should().BeFalse();
        _keys.Verify(r => r.GetActiveByKidAsync(It.IsAny<string>(), token), Times.Once);
        _jtis.Verify(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), token), Times.Once);
        _keys.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), token), Times.Once);
    }
}
