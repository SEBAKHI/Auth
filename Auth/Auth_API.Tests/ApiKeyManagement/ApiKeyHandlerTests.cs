using Auth.Application.Features.ApiKeys.CreateApiKey;
using Auth.Application.Features.ApiKeys.RevokeApiKey;
using Auth.Application.Features.ApiKeys.RotateApiKey;
using Auth.Application.Features.ApiKeys.ValidateApiKey;
using Auth.Application.Features.ApiKeys.GetApiKeys;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// Unit tests for CreateApiKeyCommandHandler.
/// </summary>
public class CreateApiKeyCommandHandlerTests
{
    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IApiKeyGenerator> _apiKeyGeneratorMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ILogger<CreateApiKeyCommandHandler>> _loggerMock;
    private readonly CreateApiKeyCommandHandler _handler;

    public CreateApiKeyCommandHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _apiKeyGeneratorMock = new Mock<IApiKeyGenerator>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<CreateApiKeyCommandHandler>>();

        _handler = new CreateApiKeyCommandHandler(
            _apiKeyRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _apiKeyGeneratorMock.Object,
            _publisherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesApiKeyAndReturnsResponse()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: applicationId);

        var command = new CreateApiKeyCommand(
            ApplicationId: applicationId,
            Name: "Test Key")
        { CreatedBy = createdBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _apiKeyGeneratorMock
            .Setup(g => g.Generate(It.IsAny<string>()))
            .Returns(("ak_prod_plainkey123", "ak_prod_", "hashed_key_value"));

        _apiKeyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey key, CancellationToken _) => key);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.ApiKey.Should().Be("ak_prod_plainkey123");
        result.Value.KeyPrefix.Should().Be("ak_prod_");

        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<ApiKeyCreatedEvent>(e =>
                    e.ApplicationId == applicationId &&
                    e.Name == "Test Key" &&
                    e.CreatedBy == createdBy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsError()
    {
        // Arrange
        var command = new CreateApiKeyCommand(
            ApplicationId: Guid.NewGuid(),
            Name: "Test Key")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(command.ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for RevokeApiKeyCommandHandler.
/// </summary>
public class RevokeApiKeyCommandHandlerTests
{
    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ILogger<RevokeApiKeyCommandHandler>> _loggerMock;
    private readonly RevokeApiKeyCommandHandler _handler;

    public RevokeApiKeyCommandHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<RevokeApiKeyCommandHandler>>();

        _handler = new RevokeApiKeyCommandHandler(
            _apiKeyRepositoryMock.Object,
            _publisherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidKey_RevokesAndPublishesEvent()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var revokedBy = Guid.NewGuid();
        var apiKey = TestHelpers.CreateApiKey(id: apiKeyId);

        var command = new RevokeApiKeyCommand(Id: apiKeyId, Reason: "No longer needed")
        { RevokedBy = revokedBy };

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _apiKeyRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<ApiKeyRevokedEvent>(e =>
                    e.ApiKeyId == apiKeyId &&
                    e.RevokedBy == revokedBy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError()
    {
        // Arrange
        var command = new RevokeApiKeyCommand(Id: Guid.NewGuid())
        { RevokedBy = Guid.NewGuid() };

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task Handle_AlreadyRevoked_ReturnsConflictError()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var apiKey = TestHelpers.CreateApiKey(
            id: apiKeyId,
            revokedAt: DateTime.UtcNow.AddHours(-1),
            revokedBy: Guid.NewGuid(),
            revokeReason: "Previously revoked");

        var command = new RevokeApiKeyCommand(Id: apiKeyId)
        { RevokedBy = Guid.NewGuid() };

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("ApiKey.AlreadyRevoked");

        _apiKeyRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for RotateApiKeyCommandHandler.
/// </summary>
public class RotateApiKeyCommandHandlerTests
{
    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IApiKeyGenerator> _apiKeyGeneratorMock;
    private readonly Mock<ILogger<RotateApiKeyCommandHandler>> _loggerMock;
    private readonly RotateApiKeyCommandHandler _handler;

    public RotateApiKeyCommandHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _apiKeyGeneratorMock = new Mock<IApiKeyGenerator>();
        _loggerMock = new Mock<ILogger<RotateApiKeyCommandHandler>>();

        _handler = new RotateApiKeyCommandHandler(
            _apiKeyRepositoryMock.Object,
            _apiKeyGeneratorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidKey_RotatesSuccessfully()
    {
        // Arrange
        var existingKeyId = Guid.NewGuid();
        var rotatedBy = Guid.NewGuid();
        var existingKey = TestHelpers.CreateApiKey(id: existingKeyId, environment: "production");

        var command = new RotateApiKeyCommand(
            ApiKeyId: existingKeyId,
            GracePeriodMinutes: 60,
            RotatedBy: rotatedBy);

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(existingKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingKey);

        _apiKeyGeneratorMock
            .Setup(g => g.Generate("production"))
            .Returns(("ak_prod_newkey456", "ak_prod_", "new_hashed_key"));

        _apiKeyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey key, CancellationToken _) => key);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.NewApiKey.Should().Be("ak_prod_newkey456");
        result.Value.NewKeyPrefix.Should().Be("ak_prod_");
        result.Value.OldApiKeyId.Should().Be(existingKeyId);

        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _apiKeyRepositoryMock.Verify(
            r => r.UpdateAsync(
                It.Is<ApiKey>(k => k.Id == existingKeyId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError()
    {
        // Arrange
        var command = new RotateApiKeyCommand(
            ApiKeyId: Guid.NewGuid(),
            GracePeriodMinutes: 60,
            RotatedBy: Guid.NewGuid());

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(command.ApiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("ApiKey.NotFound");
    }

    [Fact]
    public async Task Handle_RevokedKey_ReturnsError()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var revokedKey = TestHelpers.CreateApiKey(
            id: apiKeyId,
            revokedAt: DateTime.UtcNow.AddHours(-1),
            revokedBy: Guid.NewGuid());

        var command = new RotateApiKeyCommand(
            ApiKeyId: apiKeyId,
            GracePeriodMinutes: 60,
            RotatedBy: Guid.NewGuid());

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedKey);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Code.Should().Be("ApiKey.AlreadyRevoked");

        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for ValidateApiKeyQueryHandler.
/// </summary>
public class ValidateApiKeyQueryHandlerTests
{
    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILogger<ValidateApiKeyQueryHandler>> _loggerMock;
    private readonly ValidateApiKeyQueryHandler _handler;

    public ValidateApiKeyQueryHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loggerMock = new Mock<ILogger<ValidateApiKeyQueryHandler>>();

        _handler = new ValidateApiKeyQueryHandler(
            _apiKeyRepositoryMock.Object,
            _passwordHasherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidKey_ReturnsValidResponse()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var apiKey = TestHelpers.CreateApiKey(
            applicationId: applicationId,
            keyPrefix: "ak_prod_",
            keyHash: "stored_hash",
            name: "Production Key",
            environment: "production",
            rateLimitPerMinute: 100,
            rateLimitPerDay: 50000);

        var rawKey = "ak_prod_somesecretvalue";
        var query = new ValidateApiKeyQuery(rawKey);

        _apiKeyRepositoryMock
            .Setup(r => r.GetActiveByPrefixAsync("ak_prod_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiKey> { apiKey });

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(rawKey, "stored_hash"))
            .Returns(true);

        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(apiKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "read:users", "write:users" });

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.Name.Should().Be("Production Key");
        result.Value.Environment.Should().Be("production");
        result.Value.RateLimitPerMinute.Should().Be(100);
        result.Value.RateLimitPerDay.Should().Be(50000);
        result.Value.Scopes.Should().Contain("read:users");
        result.Value.Scopes.Should().Contain("write:users");

        _apiKeyRepositoryMock.Verify(
            r => r.RecordUsageAsync(apiKey.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidKey_ReturnsError()
    {
        // Arrange
        var apiKey = TestHelpers.CreateApiKey(
            keyPrefix: "ak_prod_",
            keyHash: "stored_hash");

        var rawKey = "ak_prod_wrongsecretvalue";
        var query = new ValidateApiKeyQuery(rawKey);

        _apiKeyRepositoryMock
            .Setup(r => r.GetActiveByPrefixAsync("ak_prod_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiKey> { apiKey });

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(rawKey, "stored_hash"))
            .Returns(false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Code.Should().Be("ApiKey.Invalid");

        _apiKeyRepositoryMock.Verify(
            r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for GetApiKeysQueryHandler.
/// </summary>
public class GetApiKeysQueryHandlerTests
{
    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly GetApiKeysQueryHandler _handler;

    public GetApiKeysQueryHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _handler = new GetApiKeysQueryHandler(
            _apiKeyRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _userRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidApplicationId_ReturnsApiKeys()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var apiKey1 = TestHelpers.CreateApiKey(
            applicationId: applicationId,
            name: "Key One",
            keyPrefix: "ak_prod_",
            environment: "production");
        var apiKey2 = TestHelpers.CreateApiKey(
            applicationId: applicationId,
            name: "Key Two",
            keyPrefix: "ak_stg_",
            environment: "staging");

        var query = new GetApiKeysQuery(applicationId);

        _apiKeyRepositoryMock
            .Setup(r => r.ListAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiKey> { apiKey1, apiKey2 });

        _applicationRepositoryMock
            .Setup(r => r.GetByIdIncludingDeletedAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, name: "Keyed App"));

        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>>
            {
                [apiKey1.Id] = ["read:users"],
                [apiKey2.Id] = ["read:users", "write:users"]
            });

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);

        result.Value[0].Name.Should().Be("Key One");
        result.Value[0].ApplicationId.Should().Be(applicationId);
        result.Value[0].ApplicationName.Should().Be("Keyed App");
        result.Value[0].KeyPrefix.Should().Be("ak_prod_");
        result.Value[0].Environment.Should().Be("production");
        result.Value[0].Scopes.Should().HaveCount(1);

        result.Value[1].Name.Should().Be("Key Two");
        result.Value[1].KeyPrefix.Should().Be("ak_stg_");
        result.Value[1].Environment.Should().Be("staging");
        result.Value[1].Scopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoApplicationId_ListsEveryApplicationsKeys()
    {
        var appOne = Guid.NewGuid();
        var appTwo = Guid.NewGuid();
        var keys = new List<ApiKey>
        {
            TestHelpers.CreateApiKey(applicationId: appOne, name: "One"),
            TestHelpers.CreateApiKey(applicationId: appTwo, name: "Two")
        };

        _apiKeyRepositoryMock
            .Setup(r => r.ListAsync(null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>>());

        var result = await _handler.Handle(new GetApiKeysQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Select(k => k.ApplicationId).Should().BeEquivalentTo([appOne, appTwo]);
    }

    [Fact]
    public async Task Handle_ResolvesScopesInOneRoundTrip()
    {
        // The guard against the N+1 returning. Scoped to one application the per-key call
        // was invisible; spanning every application it is one query per row on the platform.
        var applicationId = Guid.NewGuid();
        var keys = new List<ApiKey>
        {
            TestHelpers.CreateApiKey(applicationId: applicationId, name: "One"),
            TestHelpers.CreateApiKey(applicationId: applicationId, name: "Two"),
            TestHelpers.CreateApiKey(applicationId: applicationId, name: "Three")
        };

        _apiKeyRepositoryMock
            .Setup(r => r.ListAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>>());

        var result = await _handler.Handle(new GetApiKeysQuery(applicationId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _apiKeyRepositoryMock.Verify(
            r => r.GetScopesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _apiKeyRepositoryMock.Verify(
            r => r.GetScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_KeyWithNoScopes_ReturnsEmptyScopeList()
    {
        // Keys with no scopes are absent from the batch map; the handler must not throw
        // on the missing entry, and must not report null scopes to the client.
        var applicationId = Guid.NewGuid();
        var apiKey = TestHelpers.CreateApiKey(applicationId: applicationId, name: "Scopeless");

        _apiKeyRepositoryMock
            .Setup(r => r.ListAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiKey> { apiKey });
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>>());

        var result = await _handler.Handle(new GetApiKeysQuery(applicationId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().ContainSingle().Which.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void Validator_AcceptsNoApplicationIdButRejectsAnEmptyOne()
    {
        var validator = new GetApiKeysQueryValidator();

        validator.Validate(new GetApiKeysQuery()).IsValid.Should().BeTrue();
        validator.Validate(new GetApiKeysQuery(Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(new GetApiKeysQuery(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
