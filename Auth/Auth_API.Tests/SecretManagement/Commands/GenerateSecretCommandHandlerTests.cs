using Auth.Application.Features.Secrets.GenerateGatewayToken;
using Auth.Application.Features.Secrets.GenerateHmacKey;
using Auth.Application.Features.Secrets.GenerateRsaKey;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// The three regenerate handlers. There is no unconfirmed path to rotating a
/// platform key: each handler spends the step-up approval before it touches the
/// secret store, and refuses outright without one.
/// </summary>
public sealed class GenerateSecretCommandHandlerTests : IDisposable
{
    private readonly SecretChallengeTestContext _challenges = new();
    private readonly KeyringTestHarness _keyring = new();
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    public GenerateSecretCommandHandlerTests()
    {
        _secretServiceMock
            .Setup(s => s.GenerateGatewayTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-gateway-token");
    }

    // The RSA regenerate is an alias of a stage and an emergency promote (S12): it runs over a
    // real secrets file, with the approvals of the keyring harness.
    private GenerateRsaKeyCommandHandler RsaHandler() => new(
        _keyring.SigningService,
        new Auth.Infrastructure.Security.SharedKeyMaterialGenerator());

    public void Dispose() => _keyring.Dispose();

    // The HMAC regenerate is an alias of an emergency promote (S11): it runs over a real
    // secrets file, with the approvals of the keyring harness.
    private GenerateHmacKeyCommandHandler HmacHandler() => new(
        _keyring.Service,
        new Auth.Infrastructure.Security.SharedKeyMaterialGenerator());

    private GenerateGatewayTokenCommandHandler GatewayHandler() => new(
        _secretServiceMock.Object,
        _challenges.Service,
        _publisherMock.Object,
        new Mock<ILogger<GenerateGatewayTokenCommandHandler>>().Object);

    [Fact]
    public async Task Rsa_WithApproval_RotatesAndRecordsTheOperation()
    {
        var before = _keyring.ReadSigningRing();
        var challengeId = Guid.NewGuid();
        // Today's digest for a generate: none (the alias keeps a cached console working).
        _keyring.Challenges.WithApproval(challengeId, SecretOperation.GenerateRsaKey, _keyring.Admin);

        var result = await RsaHandler().Handle(
            new GenerateRsaKeyCommand(challengeId, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse(string.Join("; ", result.ErrorsOrEmptyList.Select(e => e.Code)));
        var after = _keyring.ReadSigningRing();
        after.Current.Kid.Should().NotBe(before.Current.Kid, "the alias installs a new key at once in the file");
        after.Current.PublicKeyPem.Should().Be(result.Value, "the answer is the new key's public key, as before");
        after.Find(before.Current.Kid)!.Compromised.Should().BeTrue("the replaced key is closed, as an emergency closes it");
        _keyring.SigningKeys.Current.Key.KeyId.Should().Be(before.Current.Kid,
            "this process never loaded the new key: it signs with it from its next start, as generate/rsa always did");
        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.GenerateRsaKey
                    && e.Action == KeyringAction.PromoteEmergency
                    && e.KeyId == after.Current.Kid
                    && e.ExecutedBy == _keyring.Admin),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "rotating the key that signs every access token must leave an audit trail");
    }

    [Fact]
    public async Task Rsa_WithoutApproval_NeverTouchesTheSecretStore()
    {
        var before = _keyring.ReadSigningRing();

        var result = await RsaHandler().Handle(
            new GenerateRsaKeyCommand(Guid.NewGuid(), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadSigningRing().Current.Kid.Should().Be(before.Current.Kid);
        _keyring.Publisher.Verify(
            p => p.Publish(It.IsAny<SecretOperationExecutedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Hmac_WithApproval_RotatesAndRecordsTheOperation()
    {
        // Today's confirmation: the generate operation, no digest.
        var challengeId = Guid.NewGuid();
        _keyring.Challenges.WithApproval(challengeId, SecretOperation.GenerateHmacKey, _keyring.Admin);

        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(challengeId, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var ring = _keyring.ReadFileRing();
        ring.Current.Id.Should().Be(2, "the alias promotes a new key at once");
        ring.Find(1)!.Compromised.Should().BeTrue("and keeps today's meaning: the old key stops verifying");
        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.GenerateHmacKey && e.ExecutedBy == _keyring.Admin),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Hmac_WithoutApproval_NeverTouchesTheSecretStore()
    {
        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(Guid.NewGuid(), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Hmac_AKeyringPromoteApproval_IsNotSpentOnTheAlias()
    {
        // The same operation with a keyring digest: a scheduled promote approved in the
        // panel must not run as an emergency through the alias.
        await _keyring.StageAsync();
        var challengeId = _keyring.Approve(KeyringAction.Promote);

        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(challengeId, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Gateway_WithApproval_RotatesAndRecordsTheOperation()
    {
        var challengeId = Guid.NewGuid();
        _challenges.WithApproval(challengeId, SecretOperation.GenerateGatewayToken, _admin);

        var result = await GatewayHandler().Handle(
            new GenerateGatewayTokenCommand(challengeId, _admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _secretServiceMock.Verify(
            s => s.GenerateGatewayTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gateway_WithoutApproval_NeverTouchesTheSecretStore()
    {
        var result = await GatewayHandler().Handle(
            new GenerateGatewayTokenCommand(Guid.NewGuid(), _admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _secretServiceMock.Verify(
            s => s.GenerateGatewayTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnApprovalForOneKey_CannotRotateAnother()
    {
        // The confirmation was raised for the gateway token, which invalidates no
        // user credential at all. It must not rotate the HMAC key, which signs
        // every user out and kills every pending password-reset link.
        var challengeId = Guid.NewGuid();
        _keyring.Challenges.WithApproval(challengeId, SecretOperation.GenerateGatewayToken, _keyring.Admin);

        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(challengeId, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }
}
