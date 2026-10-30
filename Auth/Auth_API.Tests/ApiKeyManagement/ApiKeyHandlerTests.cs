using Auth.Application.Common;
using Auth.Application.Features.ApiKeys.Common;
using Auth.Application.Features.ApiKeys.CreateApiKey;
using Auth.Application.Features.ApiKeys.RevokeApiKey;
using Auth.Application.Features.ApiKeys.RotateApiKey;
using Auth.Application.Features.ApiKeys.ValidateApiKey;
using Auth.Application.Features.ApiKeys.GetApiKeys;
using Auth.Application.Features.MachineCredentialChecks;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth.Domain.Services;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// Unit tests for CreateApiKeyCommandHandler (OI-128 PR B: a key carries at least one scope, each
/// an active permission of its own application, held by its creator; its addresses are
/// validated; the key and its scopes are one write).
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

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _createdBy = Guid.NewGuid();
    private readonly Permission _ownPermission;

    public CreateApiKeyCommandHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _apiKeyGeneratorMock = new Mock<IApiKeyGenerator>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<CreateApiKeyCommandHandler>>();

        // The policy and the guard are concrete collaborators over the same permission repository,
        // so these tests exercise the real rules rather than stubs of them.
        _handler = new CreateApiKeyCommandHandler(
            _apiKeyRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _apiKeyGeneratorMock.Object,
            new ApiKeyScopePolicy(_permissionRepositoryMock.Object, new PermissionGrantGuard(_permissionRepositoryMock.Object)),
            _publisherMock.Object,
            _loggerMock.Object);

        _ownPermission = TestHelpers.CreatePermission(applicationId: _applicationId, code: "orders:read");

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: _applicationId));
        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(_ownPermission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_ownPermission);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_createdBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "*" });
        _apiKeyGeneratorMock
            .Setup(g => g.Generate(It.IsAny<string>()))
            .Returns(new GeneratedApiKey("ak_prod_plainkey123", "ak_prod_", "selector16chars0", "hashed_key_value"));
        _apiKeyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey key, IReadOnlyCollection<ApiKeyScope> _, CancellationToken _) => key);
    }

    private CreateApiKeyCommand Command(IReadOnlyList<Guid>? permissionIds = null, IReadOnlyList<string>? allowedIps = null) =>
        new(ApplicationId: _applicationId, Name: "Test Key", PermissionIds: permissionIds ?? [_ownPermission.Id], AllowedIps: allowedIps)
        { CreatedBy = _createdBy };

    private void NothingWasWritten()
    {
        _apiKeyGeneratorMock.Verify(g => g.Generate(It.IsAny<string>()), Times.Never);
        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesTheKeyAndItsScopesInOneWrite()
    {
        IReadOnlyCollection<ApiKeyScope>? written = null;
        ApiKey? key = null;
        _apiKeyRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()))
            .Callback((ApiKey k, IReadOnlyCollection<ApiKeyScope> s, CancellationToken _) => { key = k; written = s; })
            .ReturnsAsync((ApiKey k, IReadOnlyCollection<ApiKeyScope> _, CancellationToken _) => k);

        var result = await _handler.Handle(Command(allowedIps: ["203.0.113.0/24", "::ffff:198.51.100.7"]), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApiKey.Should().Be("ak_prod_plainkey123");
        result.Value.KeyPrefix.Should().Be("ak_prod_");

        key!.KeySelector.Should().Be("selector16chars0");
        key.AllowedIps.Entries.Should().Equal("203.0.113.0/24", "198.51.100.7");
        written.Should().ContainSingle().Which.PermissionId.Should().Be(_ownPermission.Id);
        written!.Single().ApiKeyId.Should().Be(key.Id);

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<ApiKeyCreatedEvent>(e =>
                    e.ApplicationId == _applicationId &&
                    e.Name == "Test Key" &&
                    e.CreatedBy == _createdBy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsError()
    {
        var command = new CreateApiKeyCommand(ApplicationId: Guid.NewGuid(), Name: "Test Key", PermissionIds: [_ownPermission.Id])
        { CreatedBy = _createdBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(command.ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppEntity?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");
        NothingWasWritten();
    }

    [Fact]
    public async Task Handle_WithNoScope_ReturnsScopesRequiredError()
    {
        var result = await _handler.Handle(Command(permissionIds: []), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.ScopesRequired");
        NothingWasWritten();
    }

    public static TheoryData<string> ForeignScopes => new() { "platform", "other-application", "inactive" };

    [Theory]
    [MemberData(nameof(ForeignScopes))]
    public async Task Handle_WithAScopeNotOfTheKeysApplication_ReturnsScopeNotForApplicationError(string kind)
    {
        var permission = kind switch
        {
            "platform" => TestHelpers.CreatePermission(applicationId: null, code: "users:read"),
            "other-application" => TestHelpers.CreatePermission(applicationId: Guid.NewGuid(), code: "orders:read"),
            _ => TestHelpers.CreatePermission(applicationId: _applicationId, code: "orders:write", isActive: false),
        };
        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);

        var result = await _handler.Handle(Command(permissionIds: [_ownPermission.Id, permission.Id]), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.ScopeNotForApplication");
        NothingWasWritten();
    }

    [Fact]
    public async Task Handle_ScopeTheCreatorDoesNotHold_IsRefusedBeforeAnythingIsWritten()
    {
        // The creator holds a neighbouring area, never the one it is asking for.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_createdBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "apikeys:*", "roles:read" });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");

        // An API key is a credential relying parties carry, so a rejected request must not leave
        // a usable key behind.
        NothingWasWritten();
    }

    [Fact]
    public async Task Handle_CreatorWhoHoldsTheScopeButNotApplicationsUpdate_IsRefusedBeforeAnythingIsWritten()
    {
        // D-124-1 (a): granting an application's permission to a key asks what granting it to a
        // role asks, the code and applications:update.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_createdBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "apikeys:create", "orders:*" });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        NothingWasWritten();
    }

    [Fact]
    public async Task Handle_ScopeTheCreatorHoldsByWildcard_IsAllowed()
    {
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_createdBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orders:*", "applications:update" });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        // Wildcard semantics must keep matching the authorization handler's, or delegation breaks.
        result.IsError.Should().BeFalse();
        _apiKeyRepositoryMock.Verify(
            r => r.CreateAsync(
                It.IsAny<ApiKey>(),
                It.Is<IReadOnlyCollection<ApiKeyScope>>(scopes => scopes.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownPermissionId_IsRefusedRatherThanSilentlyDropped()
    {
        var unknown = Guid.NewGuid();
        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(unknown, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        var result = await _handler.Handle(Command(permissionIds: [_ownPermission.Id, unknown]), CancellationToken.None);

        // Dropping it used to mint a key with fewer scopes than asked for and no way to tell.
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        NothingWasWritten();
    }

    [Fact]
    public async Task Handle_WithAnInvalidAddress_ReturnsAllowedIpInvalidError()
    {
        var result = await _handler.Handle(Command(allowedIps: ["10.0.0.1/24"]), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.AllowedIpInvalid");
        NothingWasWritten();
    }

    [Fact]
    public void Validator_RefusesNoScope_TooManyOrBadAddresses_AndALongEnvironment()
    {
        var validator = new CreateApiKeyCommandValidator();
        CreateApiKeyCommand Valid() => Command();

        validator.Validate(Valid()).IsValid.Should().BeTrue();
        validator.Validate(Valid() with { PermissionIds = [] }).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("ApiKey.ScopesRequired");
        validator.Validate(Valid() with { PermissionIds = null }).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("ApiKey.ScopesRequired");
        validator.Validate(Valid() with { AllowedIps = Enumerable.Range(0, 51).Select(i => $"10.0.0.{i}").ToList() }).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("ApiKey.AllowedIpsTooMany");
        validator.Validate(Valid() with { AllowedIps = Enumerable.Range(0, 50).Select(i => $"10.0.0.{i}").ToList() })
            .IsValid.Should().BeTrue();
        validator.Validate(Valid() with { AllowedIps = ["203.0.113.5", "not-an-address"] }).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("ApiKey.AllowedIpInvalid");

        // The column is NVARCHAR(20): a longer value used to pass here and fail at the insert.
        validator.Validate(Valid() with { Environment = new string('e', 20) }).IsValid.Should().BeTrue();
        validator.Validate(Valid() with { Environment = new string('e', 21) }).Errors
            .Should().ContainSingle().Which.ErrorCode.Should().Be("Environment.TooLong");
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
/// Unit tests for RotateApiKeyCommandHandler (OI-128 PR B: the new key carries the old key's
/// scopes and addresses, asked again as at creation; the rotation is audited).
/// </summary>
public class RotateApiKeyCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock;
    private readonly Mock<IApiKeyGenerator> _apiKeyGeneratorMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ILogger<RotateApiKeyCommandHandler>> _loggerMock;
    private readonly RotateApiKeyCommandHandler _handler;

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _rotatedBy = Guid.NewGuid();
    private readonly ApiKey _existingKey;
    private readonly Permission _read;
    private readonly Permission _write;

    public RotateApiKeyCommandHandlerTests()
    {
        _apiKeyRepositoryMock = new Mock<IApiKeyRepository>();
        _apiKeyGeneratorMock = new Mock<IApiKeyGenerator>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<RotateApiKeyCommandHandler>>();

        _handler = new RotateApiKeyCommandHandler(
            _apiKeyRepositoryMock.Object,
            _apiKeyGeneratorMock.Object,
            new ApiKeyScopePolicy(_permissionRepositoryMock.Object, new PermissionGrantGuard(_permissionRepositoryMock.Object)),
            _publisherMock.Object,
            new FixedTimeProvider(Now),
            _loggerMock.Object);

        _existingKey = TestHelpers.CreateApiKey(
            applicationId: _applicationId, environment: "production", allowedIps: ["203.0.113.0/24"]);
        _read = TestHelpers.CreatePermission(applicationId: _applicationId, code: "orders:read");
        _write = TestHelpers.CreatePermission(applicationId: _applicationId, code: "orders:write");

        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(_existingKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_existingKey);
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopePermissionIdsAsync(_existingKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_read.Id, _write.Id]);
        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == _read.Id ? _read : id == _write.Id ? _write : null);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_rotatedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orders:*", "applications:update" });
        _apiKeyGeneratorMock
            .Setup(g => g.Generate(It.IsAny<string>()))
            .Returns(new GeneratedApiKey("ak_prod_newkey456", "ak_prod_", "newselector00000", "new_hashed_key"));
        _apiKeyRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<ApiKey>(), It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RotateApiKeyCommand Command(Guid? apiKeyId = null) =>
        new(ApiKeyId: apiKeyId ?? _existingKey.Id, GracePeriodMinutes: 60, RotatedBy: _rotatedBy);

    private void NoKeyWasMinted()
    {
        _apiKeyGeneratorMock.Verify(g => g.Generate(It.IsAny<string>()), Times.Never);
        _apiKeyRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<ApiKey>(), It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _publisherMock.Verify(p => p.Publish(It.IsAny<ApiKeyRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_KeyRevokedDuringTheRotation_ReturnsAlreadyRevokedError_AndPublishesNothing()
    {
        _apiKeyRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<ApiKey>(), It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.AlreadyRevoked");
        _publisherMock.Verify(p => p.Publish(It.IsAny<ApiKeyRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidKey_CopiesScopesAndAddresses_InOneWrite_AndPublishesTheEvent()
    {
        ApiKey? replaced = null;
        ApiKey? replacement = null;
        IReadOnlyCollection<ApiKeyScope>? scopes = null;
        _apiKeyRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<ApiKey>(), It.IsAny<ApiKey>(), It.IsAny<IReadOnlyCollection<ApiKeyScope>>(), It.IsAny<CancellationToken>()))
            .Callback((ApiKey a, ApiKey b, IReadOnlyCollection<ApiKeyScope> s, CancellationToken _) => { replaced = a; replacement = b; scopes = s; })
            .ReturnsAsync(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.NewApiKey.Should().Be("ak_prod_newkey456");
        result.Value.NewKeyPrefix.Should().Be("ak_prod_");
        result.Value.OldApiKeyId.Should().Be(_existingKey.Id);
        result.Value.OldKeyExpiresAt.Should().Be(Now.UtcDateTime.AddMinutes(60));

        replaced!.Id.Should().Be(_existingKey.Id);
        replaced.ExpiresAt.Should().Be(Now.UtcDateTime.AddMinutes(60));
        replacement!.ApplicationId.Should().Be(_applicationId);
        replacement.KeySelector.Should().Be("newselector00000");
        replacement.AllowedIps.Entries.Should().Equal("203.0.113.0/24");
        scopes!.Select(s => s.PermissionId).Should().BeEquivalentTo([_read.Id, _write.Id]);
        scopes!.Should().OnlyContain(s => s.ApiKeyId == replacement.Id && s.GrantedBy == _rotatedBy);

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<ApiKeyRotatedEvent>(e =>
                    e.ApiKeyId == replacement.Id
                    && e.ReplacedApiKeyId == _existingKey.Id
                    && e.ApplicationId == _applicationId
                    && e.ReplacedKeyExpiresAt == Now.UtcDateTime.AddMinutes(60)
                    && e.TriggeredBy == _rotatedBy
                    && e.OccurredAt == Now
                    && e.EventId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ScopelessKey_ReturnsScopesRequiredError()
    {
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopePermissionIdsAsync(_existingKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.ScopesRequired");
        NoKeyWasMinted();
    }

    [Fact]
    public async Task Handle_RotatorWhoLacksAScope_ReturnsCannotGrantHigherError()
    {
        // Holds apikeys:rotate (the endpoint's gate), applications:update and orders:read, but
        // not orders:write.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_rotatedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "apikeys:rotate", "applications:update", "orders:read" });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        NoKeyWasMinted();
    }

    [Fact]
    public async Task Handle_RotatorWhoHoldsTheScopesButNotApplicationsUpdate_ReturnsCannotGrantHigherError()
    {
        // D-124-1 (a): rotation re-grants the scopes, so it asks what creation asks.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_rotatedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "apikeys:rotate", "orders:*" });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        NoKeyWasMinted();
    }

    [Fact]
    public async Task Handle_KeyNotFound_ReturnsError()
    {
        var command = Command(Guid.NewGuid());
        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(command.ApiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("ApiKey.NotFound");
        NoKeyWasMinted();
    }

    [Fact]
    public async Task Handle_RevokedKey_ReturnsError()
    {
        var revokedKey = TestHelpers.CreateApiKey(
            applicationId: _applicationId,
            revokedAt: DateTime.UtcNow.AddHours(-1),
            revokedBy: Guid.NewGuid());
        _apiKeyRepositoryMock
            .Setup(r => r.GetByIdAsync(revokedKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedKey);

        var result = await _handler.Handle(Command(revokedKey.Id), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("ApiKey.AlreadyRevoked");
        NoKeyWasMinted();
    }
}

/// <summary>
/// Unit tests for ValidateApiKeyQueryHandler (OI-128 PR B): the caller first, then the key's
/// shape and check, then the one row its selector names, then the binding, then ONE verify.
/// </summary>
public class ValidateApiKeyQueryHandlerTests
{
    private const string Code = "CRM-WEB";

    private readonly Mock<IApiKeyRepository> _apiKeyRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly ValidateApiKeyQueryHandler _handler;

    private readonly AppEntity _application = TestHelpers.CreateApplication(code: Code);
    private readonly string _rawKey = ApiKeyFormat.Compose("production", "Sel3ctor0000000A", new string('s', 32));
    private readonly ApiKey _apiKey;

    public ValidateApiKeyQueryHandlerTests()
    {
        _handler = new ValidateApiKeyQueryHandler(
            _apiKeyRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _passwordHasherMock.Object,
            Mock.Of<ILogger<ValidateApiKeyQueryHandler>>());

        _apiKey = TestHelpers.CreateApiKey(
            applicationId: _application.Id,
            keyPrefix: "ak_prod_",
            keySelector: "Sel3ctor0000000A",
            keyHash: "stored_hash",
            name: "Production Key",
            environment: "production",
            allowedIps: ["203.0.113.0/24"]);

        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);
        _apiKeyRepositoryMock
            .Setup(r => r.GetActiveBySelectorAsync("Sel3ctor0000000A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_apiKey);
        _apiKeyRepositoryMock
            .Setup(r => r.GetScopesAsync(_apiKey.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orders:read" });
        _passwordHasherMock
            .Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string hash) => key == _rawKey && hash == "stored_hash");
    }

    private static ValidateApiKeyQuery Query(string rawKey, string? scope = MachineClients.Scopes.ApiKeysValidate, string code = Code) =>
        new(new MachineCredentialCaller(code, scope), rawKey);

    private void VerifiesCalled(int times) =>
        _passwordHasherMock.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(times));

    private void NoUseRecorded() =>
        _apiKeyRepositoryMock.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Handle_OwnKey_ValidatesWithExactlyOneVerify_AndReportsCodeAndAddresses()
    {
        var result = await _handler.Handle(Query(_rawKey), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        result.Value.ApiKeyId.Should().Be(_apiKey.Id);
        result.Value.ApplicationId.Should().Be(_application.Id);
        result.Value.ApplicationCode.Should().Be(Code);
        result.Value.Name.Should().Be("Production Key");
        result.Value.Environment.Should().Be("production");
        result.Value.Scopes.Should().Equal("orders:read");
        result.Value.AllowedIps.Should().Equal("203.0.113.0/24");

        VerifiesCalled(1);
        _apiKeyRepositoryMock.Verify(r => r.RecordUsageAsync(_apiKey.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherApplicationsKey_ReturnsInvalidError_BeforeTheHash_AndRecordsNoUse()
    {
        // The same key, presented by another application's server.
        var other = TestHelpers.CreateApplication(code: "OTHER-APP");
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("OTHER-APP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await _handler.Handle(Query(_rawKey, code: "OTHER-APP"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(Auth.Domain.Errors.ApiKeyErrors.Invalid);
        VerifiesCalled(0);
        NoUseRecorded();
        _apiKeyRepositoryMock.Verify(r => r.GetScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownSelector_ReturnsInvalidError_WithNoVerify()
    {
        var unknown = ApiKeyFormat.Compose("production", "Unkn0wn000000000", new string('s', 32));

        var result = await _handler.Handle(Query(unknown), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.Invalid");
        VerifiesCalled(0);
        NoUseRecorded();
    }

    [Fact]
    public async Task Handle_WrongSecret_ReturnsInvalidError_AfterOneVerify()
    {
        var wrong = ApiKeyFormat.Compose("production", "Sel3ctor0000000A", new string('w', 32));

        var result = await _handler.Handle(Query(wrong), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.Invalid");
        VerifiesCalled(1);
        NoUseRecorded();
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("legacy")]
    [InlineData("shape")]
    public async Task Handle_MalformedKey_ReturnsInvalidError_WithoutReachingTheDatabase(string kind)
    {
        var raw = kind switch
        {
            // One character of the check changed.
            "checksum" => _rawKey[..^1] + (_rawKey[^1] == 'A' ? 'B' : 'A'),
            // A key minted before the selector format: no fallback path.
            "legacy" => "ak_prod_" + new string('x', 32),
            _ => "ak_prod_Sel3ctor0000000A_short",
        };

        var result = await _handler.Handle(Query(raw), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ApiKey.Invalid");
        _apiKeyRepositoryMock.VerifyNoOtherCalls();
        VerifiesCalled(0);
    }

    [Theory]
    [InlineData("", "ApiKey.Required")]
    [InlineData("wk_something", "ApiKey.InvalidPrefix")]
    public async Task Handle_EmptyOrForeignPrefix_KeepsTodaysAnswers(string raw, string code)
    {
        var result = await _handler.Handle(Query(raw), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _apiKeyRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_CallerWithoutTheScope_ReturnsInsufficientScope_BeforeTheKeyIsRead()
    {
        var result = await _handler.Handle(Query(_rawKey, scope: MachineClients.Scopes.WebhookKeysValidate), CancellationToken.None);

        result.FirstError.Code.Should().Be("Http.Forbidden");
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        _apiKeyRepositoryMock.VerifyNoOtherCalls();
        VerifiesCalled(0);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task Handle_CallerWhoseApplicationIsGone_ReturnsTokenRevoked(string kind)
    {
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kind == "inactive" ? TestHelpers.CreateApplication(code: Code, isActive: false) : null);

        var result = await _handler.Handle(Query(_rawKey), CancellationToken.None);

        result.FirstError.Code.Should().Be("Http.TokenRevoked");
        result.FirstError.Type.Should().Be(ErrorType.Unauthorized);
        _apiKeyRepositoryMock.VerifyNoOtherCalls();
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
