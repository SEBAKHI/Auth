using System.Reflection;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.MachineKeys.CreateMachineKey;
using Auth.Application.Features.MachineKeys.GetMachineKeys;
using Auth.Application.Features.MachineKeys.RevokeMachineKey;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.MachineKeys;

/// <summary>
/// The three machine-key use cases (OI-128, T5) with the real key-material service and mocked
/// stores, each Setup and Verify passing It.IsAny for every parameter.
/// </summary>
public class MachineKeyHandlerTests
{
    private const string BaseUrl = "https://auth.example.com";

    private readonly Application _application = TestHelpers.CreateApplication(code: "shop");
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Guid _admin = Guid.NewGuid();
    private ApplicationMachineKey? _stored;

    public MachineKeyHandlerTests()
    {
        _applications.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == _application.Id ? _application : null);
        _keys.Setup(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationMachineKey, int, CancellationToken>((key, _, _) => _stored = key)
            .ReturnsAsync(MachineKeyCreateOutcome.Created);
    }

    private CreateMachineKeyCommandHandler CreateHandler() => new(
        _applications.Object,
        _keys.Object,
        new MachineKeyMaterial(),
        _publisher.Object,
        TimeProvider.System,
        TestHelpers.CreateOptions(new JwtSettings { Issuer = "https://auth.example.com" }),
        NullLogger<CreateMachineKeyCommandHandler>.Instance);

    private Task<ErrorOr<CreateMachineKeyResponse>> CreateAsync(string? publicKeyJwk = null, Guid? applicationId = null) =>
        CreateHandler().Handle(
            new CreateMachineKeyCommand(applicationId ?? _application.Id, "server", publicKeyJwk, BaseUrl) { CreatedBy = _admin },
            CancellationToken.None);

    // --- create ---

    [Fact]
    public async Task Handle_Generate_ReturnsThePrivateKeyOnce_AndStoresNoPrivateMember()
    {
        var result = await CreateAsync();

        result.IsError.Should().BeFalse();
        var response = result.Value;
        response.PrivateKeyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        response.ClientId.Should().Be("shop");
        response.Issuer.Should().Be("https://auth.example.com");
        response.TokenEndpoint.Should().Be("https://auth.example.com/api/v1/auth/token");
        response.Algorithm.Should().Be("ES256");
        response.Kid.Should().Be(_stored!.Kid);

        using var stored = JsonDocument.Parse(_stored.PublicKeyJwk);
        stored.RootElement.TryGetProperty("d", out _).Should().BeFalse("the stored JWK has no private member");
        stored.RootElement.EnumerateObject().Select(member => member.Name).Should().Equal("crv", "kty", "x", "y");
    }

    [Fact]
    public async Task Handle_PastedPublicKey_StoresIt_AndReturnsNoPrivateKey()
    {
        var pasted = new MachineKeyMaterial().Generate().PublicKey;

        var result = await CreateAsync(pasted.PublicKeyJwk);

        result.IsError.Should().BeFalse();
        result.Value.PrivateKeyPem.Should().BeNull();
        result.Value.Kid.Should().Be(pasted.Kid);
        _stored!.PublicKeyJwk.Should().Be(pasted.PublicKeyJwk);
    }

    [Fact]
    public async Task Handle_PastedPrivateKey_IsInvalidPublicKey_AndStoresNothing()
    {
        var pem = new MachineKeyMaterial().Generate().PrivateKeyPem;
        using var ecdsa = System.Security.Cryptography.ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        var jwk = JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            x = System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.X),
            y = System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.Y),
            d = System.Buffers.Text.Base64Url.EncodeToString(parameters.D)
        });

        var result = await CreateAsync(jwk);

        result.FirstError.Code.Should().Be(MachineKeyErrors.InvalidPublicKey.Code);
        _keys.Verify(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AFourthActiveKey_IsLimitReached_AndPublishesNothing()
    {
        _keys.Setup(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MachineKeyCreateOutcome.LimitReached);

        var result = await CreateAsync();

        result.FirstError.Code.Should().Be(MachineKeyErrors.LimitReached.Code);
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        _keys.Verify(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), 3, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<MachineKeyCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_APublicKeyAlreadyRegistered_IsInvalidPublicKey()
    {
        _keys.Setup(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MachineKeyCreateOutcome.DuplicateKey);

        var result = await CreateAsync(new MachineKeyMaterial().Generate().PublicKey.PublicKeyJwk);

        result.FirstError.Code.Should().Be(MachineKeyErrors.InvalidPublicKey.Code);
    }

    [Fact]
    public async Task Handle_AnUnknownApplication_IsNotFound()
    {
        var result = await CreateAsync(applicationId: Guid.NewGuid());

        result.FirstError.Code.Should().Be("Application.NotFound");
        _keys.Verify(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Created_PublishesTheEventAfterTheWrite_WithoutTheRequestToken_AndWithoutKeyMaterial()
    {
        using var source = new CancellationTokenSource();
        MachineKeyCreatedEvent? published = null;
        CancellationToken publishToken = source.Token;
        _publisher.Setup(p => p.Publish(It.IsAny<MachineKeyCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MachineKeyCreatedEvent, CancellationToken>((e, t) => { published = e; publishToken = t; })
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new CreateMachineKeyCommand(_application.Id, null, null, BaseUrl) { CreatedBy = _admin }, source.Token);

        result.IsError.Should().BeFalse();
        _keys.Verify(r => r.TryCreateAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<int>(), source.Token), Times.Once);
        publishToken.Should().Be(CancellationToken.None, "the key exists: a client disconnect must not cancel its audit");
        published!.MachineKeyId.Should().Be(result.Value.Id);
        published.Kid.Should().Be(result.Value.Kid);
        published.Generated.Should().BeTrue();
        published.TriggeredBy.Should().Be(_admin);
        JsonSerializer.Serialize(published).Should().NotContain("PRIVATE KEY").And.NotContain("\"d\"");
    }

    [Fact]
    public void TheEvents_CarryNoKeyMaterial()
    {
        foreach (var type in new[] { typeof(MachineKeyCreatedEvent), typeof(MachineKeyRevokedEvent) })
        {
            type.GetProperties().Select(p => p.Name)
                .Should().NotContain(name => name.Contains("Pem") || name.Contains("Jwk") || name.Contains("Private"),
                    "{0} must name the key, never carry it", type.Name);
        }
    }

    // --- list ---

    [Fact]
    public async Task Handle_List_NeverReturnsKeyMaterial()
    {
        var generated = new MachineKeyMaterial().Generate();
        var key = ApplicationMachineKey.Create(
            _application.Id, generated.PublicKey.Kid, generated.PublicKey.PublicKeyJwk, "server", _admin, DateTime.UtcNow).Value;
        _keys.Setup(r => r.ListByApplicationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([key]);

        var result = await new GetMachineKeysQueryHandler(_applications.Object, _keys.Object)
            .Handle(new GetMachineKeysQuery(_application.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain(generated.PublicKey.Kid);
        using var stored = JsonDocument.Parse(generated.PublicKey.PublicKeyJwk);
        json.Should().NotContain(stored.RootElement.GetProperty("x").GetString(), "not even the public key is listed");
        json.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public void TheListedShape_HasNoKeyMaterialMember()
    {
        // The break this guards: a PrivateKeyPem or PublicKeyJwk member added to the listing.
        typeof(MachineKeyDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Pem") || name.Contains("Jwk") || name.Contains("Private") || name.Contains("PublicKey"));
    }

    [Fact]
    public async Task Handle_ListOfAnUnknownApplication_IsNotFound()
    {
        var result = await new GetMachineKeysQueryHandler(_applications.Object, _keys.Object)
            .Handle(new GetMachineKeysQuery(Guid.NewGuid()), CancellationToken.None);

        result.FirstError.Code.Should().Be("Application.NotFound");
    }

    // --- revoke ---

    private RevokeMachineKeyCommandHandler RevokeHandler() => new(
        _keys.Object, _publisher.Object, TimeProvider.System, NullLogger<RevokeMachineKeyCommandHandler>.Instance);

    private ApplicationMachineKey ActiveKey() =>
        ApplicationMachineKey.Create(_application.Id, "kid-1", "{\"crv\":\"P-256\"}", null, _admin, DateTime.UtcNow).Value;

    [Fact]
    public async Task Handle_Revoke_WritesConditionally_AndPublishes()
    {
        var key = ActiveKey();
        _keys.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(key);
        _keys.Setup(r => r.TryRevokeAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await RevokeHandler().Handle(
            new RevokeMachineKeyCommand(_application.Id, key.Id) { RevokedBy = _admin }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        key.IsRevoked.Should().BeTrue();
        key.RevokedBy.Should().Be(_admin);
        _keys.Verify(r => r.GetAsync(_application.Id, key.Id, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(
            It.Is<MachineKeyRevokedEvent>(e => e.MachineKeyId == key.Id && e.Kid == "kid-1" && e.TriggeredBy == _admin),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_RevokeAKeyOfAnotherApplication_IsNotFound()
    {
        // The store scopes the read to the route's application: another's key reads as none.
        _keys.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationMachineKey?)null);

        var result = await RevokeHandler().Handle(
            new RevokeMachineKeyCommand(_application.Id, Guid.NewGuid()) { RevokedBy = _admin }, CancellationToken.None);

        result.FirstError.Code.Should().Be(MachineKeyErrors.NotFound.Code);
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_RevokeARevokedKey_IsAlreadyRevoked()
    {
        var key = ActiveKey();
        key.Revoke(_admin, null, DateTime.UtcNow);
        _keys.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await RevokeHandler().Handle(
            new RevokeMachineKeyCommand(_application.Id, key.Id) { RevokedBy = _admin }, CancellationToken.None);

        result.FirstError.Code.Should().Be(MachineKeyErrors.AlreadyRevoked.Code);
        _keys.Verify(r => r.TryRevokeAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RevokeLostToAConcurrentRevoke_IsAlreadyRevoked_AndPublishesNothing()
    {
        _keys.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ActiveKey());
        _keys.Setup(r => r.TryRevokeAsync(It.IsAny<ApplicationMachineKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await RevokeHandler().Handle(
            new RevokeMachineKeyCommand(_application.Id, Guid.NewGuid()) { RevokedBy = _admin }, CancellationToken.None);

        result.FirstError.Code.Should().Be(MachineKeyErrors.AlreadyRevoked.Code);
        _publisher.Verify(p => p.Publish(It.IsAny<MachineKeyRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- the entity ---

    [Fact]
    public void Create_RefusesAnEmptyKidOrKey()
    {
        ApplicationMachineKey.Create(_application.Id, "", "{}", null, _admin, DateTime.UtcNow).IsError.Should().BeTrue();
        ApplicationMachineKey.Create(_application.Id, "kid", " ", null, _admin, DateTime.UtcNow).IsError.Should().BeTrue();
        ApplicationMachineKey.Create(Guid.Empty, "kid", "{}", null, _admin, DateTime.UtcNow).IsError.Should().BeTrue();
    }

    [Fact]
    public void Create_IsES256_AndTrimsTheName()
    {
        var key = ApplicationMachineKey.Create(_application.Id, "kid", "{}", "  server  ", _admin, DateTime.UtcNow).Value;

        key.Algorithm.Should().Be(MachineClients.SigningAlgorithm);
        key.Name.Should().Be("server");
        key.IsRevoked.Should().BeFalse();
    }
}
