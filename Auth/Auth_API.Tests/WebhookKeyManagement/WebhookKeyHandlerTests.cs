using Auth.Application.Features.WebhookKeys.CreateWebhookKey;
using Auth.Application.Features.WebhookKeys.RevokeWebhookKey;
using Auth.Application.Features.WebhookKeys.RotateWebhookKey;
using Auth.Application.Features.WebhookKeys.ValidateWebhookKey;
using Auth.Application.Features.WebhookKeys.GetWebhookKeys;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.WebhookKeyManagement;

public class CreateWebhookKeyCommandHandlerTests
{
    private readonly Mock<IWebhookKeyRepository> _webhookKeyRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<IWebhookKeyGenerator> _generatorMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly CreateWebhookKeyCommandHandler _handler;

    public CreateWebhookKeyCommandHandlerTests()
    {
        _handler = new CreateWebhookKeyCommandHandler(
            _webhookKeyRepoMock.Object,
            _appRepoMock.Object,
            _generatorMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<CreateWebhookKeyCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesWebhookKeyAndReturnsResponse()
    {
        var appId = Guid.NewGuid();
        var command = new CreateWebhookKeyCommand(appId, "Test Key", "https://hook.test.com/cb")
        { CreatedBy = Guid.NewGuid() };

        _appRepoMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId));

        _generatorMock
            .Setup(g => g.Generate(It.IsAny<string>()))
            .Returns(("wk_prod_plainkey123", "wk_prod_", "hashedkey123"));

        _webhookKeyRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<WebhookKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookKey wk, CancellationToken _) => wk);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.WebhookKey.Should().Be("wk_prod_plainkey123");
        _webhookKeyRepoMock.Verify(r => r.CreateAsync(It.IsAny<WebhookKey>(), It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<WebhookKeyCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsError()
    {
        var command = new CreateWebhookKeyCommand(Guid.NewGuid(), "Test", "https://hook.test.com")
        { CreatedBy = Guid.NewGuid() };

        _appRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Application?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class RevokeWebhookKeyCommandHandlerTests
{
    private readonly Mock<IWebhookKeyRepository> _repoMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly RevokeWebhookKeyCommandHandler _handler;

    public RevokeWebhookKeyCommandHandlerTests()
    {
        _handler = new RevokeWebhookKeyCommandHandler(
            _repoMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<RevokeWebhookKeyCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidKey_RevokesSuccessfully()
    {
        var keyId = Guid.NewGuid();
        var key = TestHelpers.CreateWebhookKey(id: keyId);
        var command = new RevokeWebhookKeyCommand(keyId, "Security") { RevokedBy = Guid.NewGuid() };

        _repoMock.Setup(r => r.GetByIdAsync(keyId, It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _repoMock.Verify(r => r.UpdateAsync(key, It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<WebhookKeyRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError()
    {
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookKey?)null);

        var result = await _handler.Handle(
            new RevokeWebhookKeyCommand(Guid.NewGuid()) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_AlreadyRevoked_ReturnsConflictError()
    {
        var keyId = Guid.NewGuid();
        var key = TestHelpers.CreateWebhookKey(id: keyId, revokedAt: DateTime.UtcNow, revokedBy: Guid.NewGuid());

        _repoMock.Setup(r => r.GetByIdAsync(keyId, It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await _handler.Handle(
            new RevokeWebhookKeyCommand(keyId) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
    }
}

public class RotateWebhookKeyCommandHandlerTests
{
    private readonly Mock<IWebhookKeyRepository> _repoMock = new();
    private readonly Mock<IWebhookKeyGenerator> _generatorMock = new();
    private readonly RotateWebhookKeyCommandHandler _handler;

    public RotateWebhookKeyCommandHandlerTests()
    {
        _handler = new RotateWebhookKeyCommandHandler(
            _repoMock.Object,
            _generatorMock.Object,
            new Mock<ILogger<RotateWebhookKeyCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidKey_RotatesSuccessfully()
    {
        var keyId = Guid.NewGuid();
        var key = TestHelpers.CreateWebhookKey(id: keyId);

        _repoMock.Setup(r => r.GetByIdAsync(keyId, It.IsAny<CancellationToken>())).ReturnsAsync(key);
        _generatorMock.Setup(g => g.Generate(It.IsAny<string>())).Returns(("new_plain_key", "wk_prod_new_", "new_hash"));
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<WebhookKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookKey wk, CancellationToken _) => wk);

        var result = await _handler.Handle(
            new RotateWebhookKeyCommand(keyId, 60, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.NewWebhookKey.Should().Be("new_plain_key");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<WebhookKey>(), It.IsAny<CancellationToken>()), Times.Once());
        _repoMock.Verify(r => r.UpdateAsync(key, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError()
    {
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookKey?)null);

        var result = await _handler.Handle(
            new RotateWebhookKeyCommand(Guid.NewGuid(), 60, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class ValidateWebhookKeyQueryHandlerTests
{
    private readonly Mock<IWebhookKeyRepository> _repoMock = new();
    private readonly Mock<IWebhookKeyHasher> _hasherMock = new();
    private readonly ValidateWebhookKeyQueryHandler _handler;

    public ValidateWebhookKeyQueryHandlerTests()
    {
        _handler = new ValidateWebhookKeyQueryHandler(
            _repoMock.Object,
            _hasherMock.Object,
            new Mock<ILogger<ValidateWebhookKeyQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidKey_ReturnsValidResponse()
    {
        var key = TestHelpers.CreateWebhookKey(keyHash: "computed_hash");
        var query = new ValidateWebhookKeyQuery("wk_prod_rawkeyvalue");

        _hasherMock.Setup(h => h.ComputeHash("wk_prod_rawkeyvalue")).Returns("computed_hash");
        _repoMock.Setup(r => r.GetByHashAsync("computed_hash", It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        _repoMock.Verify(r => r.RecordUsageAsync(key.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_InvalidKey_ReturnsError()
    {
        _hasherMock.Setup(h => h.ComputeHash(It.IsAny<string>())).Returns("unknown_hash");
        _repoMock.Setup(r => r.GetByHashAsync("unknown_hash", It.IsAny<CancellationToken>())).ReturnsAsync((WebhookKey?)null);

        var result = await _handler.Handle(new ValidateWebhookKeyQuery("invalid_key"), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class GetWebhookKeysQueryHandlerTests
{
    private readonly Mock<IWebhookKeyRepository> _repoMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly GetWebhookKeysQueryHandler _handler;

    public GetWebhookKeysQueryHandlerTests()
    {
        _handler = new GetWebhookKeysQueryHandler(
            _repoMock.Object,
            _applicationRepositoryMock.Object,
            _userRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidApplicationId_ReturnsWebhookKeys()
    {
        var appId = Guid.NewGuid();
        var keys = new List<WebhookKey>
        {
            TestHelpers.CreateWebhookKey(applicationId: appId, name: "Key1"),
            TestHelpers.CreateWebhookKey(applicationId: appId, name: "Key2")
        };

        _repoMock.Setup(r => r.GetByApplicationAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdIncludingDeletedAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId, name: "Hooked App"));

        var result = await _handler.Handle(new GetWebhookKeysQuery(appId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(k => k.ApplicationName == "Hooked App");
    }
}
