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
public class GenerateSecretCommandHandlerTests
{
    private readonly SecretChallengeTestContext _challenges = new();
    private readonly Mock<IDpapiSecretService> _secretServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Guid _admin = Guid.NewGuid();

    public GenerateSecretCommandHandlerTests()
    {
        _secretServiceMock
            .Setup(s => s.GenerateRsaKeyPairAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("-----BEGIN PUBLIC KEY-----");
        _secretServiceMock
            .Setup(s => s.GenerateGatewayTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-gateway-token");
    }

    private GenerateRsaKeyCommandHandler RsaHandler() => new(
        _secretServiceMock.Object,
        _challenges.Service,
        _publisherMock.Object,
        new Mock<ILogger<GenerateRsaKeyCommandHandler>>().Object);

    private GenerateHmacKeyCommandHandler HmacHandler() => new(
        _secretServiceMock.Object,
        _challenges.Service,
        _publisherMock.Object,
        new Mock<ILogger<GenerateHmacKeyCommandHandler>>().Object);

    private GenerateGatewayTokenCommandHandler GatewayHandler() => new(
        _secretServiceMock.Object,
        _challenges.Service,
        _publisherMock.Object,
        new Mock<ILogger<GenerateGatewayTokenCommandHandler>>().Object);

    [Fact]
    public async Task Rsa_WithApproval_RotatesAndRecordsTheOperation()
    {
        var challengeId = Guid.NewGuid();
        _challenges.WithApproval(challengeId, SecretOperation.GenerateRsaKey, _admin);

        var result = await RsaHandler().Handle(
            new GenerateRsaKeyCommand(challengeId, _admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _secretServiceMock.Verify(
            s => s.GenerateRsaKeyPairAsync(It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.GenerateRsaKey && e.ExecutedBy == _admin),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "rotating the key that signs every access token must leave an audit trail");
    }

    [Fact]
    public async Task Rsa_WithoutApproval_NeverTouchesTheSecretStore()
    {
        var result = await RsaHandler().Handle(
            new GenerateRsaKeyCommand(Guid.NewGuid(), _admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _secretServiceMock.Verify(
            s => s.GenerateRsaKeyPairAsync(It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SecretOperationExecutedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Hmac_WithApproval_RotatesAndRecordsTheOperation()
    {
        var challengeId = Guid.NewGuid();
        _challenges.WithApproval(challengeId, SecretOperation.GenerateHmacKey, _admin);

        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(challengeId, _admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _secretServiceMock.Verify(
            s => s.GenerateHmacKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hmac_WithoutApproval_NeverTouchesTheSecretStore()
    {
        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(Guid.NewGuid(), _admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _secretServiceMock.Verify(
            s => s.GenerateHmacKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        _challenges.WithApproval(challengeId, SecretOperation.GenerateGatewayToken, _admin);

        var result = await HmacHandler().Handle(
            new GenerateHmacKeyCommand(challengeId, _admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _secretServiceMock.Verify(
            s => s.GenerateHmacKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
