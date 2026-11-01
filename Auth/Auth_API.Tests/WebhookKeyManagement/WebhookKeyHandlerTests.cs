using Auth.Application.Features.WebhookKeys.CreateWebhookKey;
using Auth.Application.Features.WebhookKeys.RevokeWebhookKey;
using Auth.Application.Features.WebhookKeys.RotateWebhookKey;
using Auth.Application.Features.WebhookKeys.ValidateWebhookKey;
using Auth.Application.Features.WebhookKeys.GetWebhookKeys;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
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

/// <summary>
/// Unit tests for ValidateWebhookKeyQueryHandler (OI-128 PR B): a webhook key is reported only
/// to its own application; another application's key is an unknown key and touches nothing.
/// </summary>
public class ValidateWebhookKeyQueryHandlerTests
{
    private const string Code = "CRM-WEB";

    private readonly Mock<IWebhookKeyRepository> _repoMock = new();
    private readonly Mock<IApplicationRepository> _applicationsMock = new();
    private readonly FakeCredentialHasher _hasherMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly ValidateWebhookKeyQueryHandler _handler;
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: Code);

    public ValidateWebhookKeyQueryHandlerTests()
    {
        _handler = new ValidateWebhookKeyQueryHandler(
            _repoMock.Object,
            _applicationsMock.Object,
            _hasherMock,
            _publisherMock.Object,
            new Mock<ILogger<ValidateWebhookKeyQueryHandler>>().Object);

        _applicationsMock.Setup(r => r.GetByCodeAsync(Code, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
    }

    private static ValidateWebhookKeyQuery Query(string raw, string scope = Auth.Domain.Constants.MachineClients.Scopes.WebhookKeysValidate) =>
        new(new Auth.Application.Features.MachineCredentialChecks.MachineCredentialCaller(Code, scope), raw);

    [Fact]
    public async Task Handle_ValidKey_ReturnsValidResponse()
    {
        var key = TestHelpers.CreateWebhookKey(applicationId: _application.Id, keyHash: "computed_hash");

        _hasherMock.Map("wk_prod_rawkeyvalue", "computed_hash");
        _repoMock.Setup(r => r.GetByHashAsync(FakeCredentialHasher.CandidatesOf("computed_hash"), It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await _handler.Handle(Query("wk_prod_rawkeyvalue"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        result.Value.ApplicationCode.Should().Be(Code);
        _repoMock.Verify(r => r.RecordUsageAsync(key.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_InvalidKey_ReturnsError()
    {
        _hasherMock.MapAll(_ => "unknown_hash");
        _repoMock.Setup(r => r.GetByHashAsync(FakeCredentialHasher.CandidatesOf("unknown_hash"), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookKey?)null);

        var result = await _handler.Handle(Query("wk_prod_unknown"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("WebhookKey.Invalid");
    }

    [Fact]
    public async Task Handle_AnotherApplicationsKey_ReturnsInvalidError_NoUsageStamp_NoRefingerprint()
    {
        // Stored under an old fingerprint, so a successful validation would ask for a rewrite.
        var key = TestHelpers.CreateWebhookKey(applicationId: Guid.NewGuid(), keyHash: "legacy-form");
        _hasherMock.Map("wk_prod_rawkeyvalue", "issuing-form");
        _repoMock.Setup(r => r.GetByHashAsync(It.IsAny<Auth.Domain.ValueObjects.CredentialHashCandidates>(), It.IsAny<CancellationToken>())).ReturnsAsync(key);

        var result = await _handler.Handle(Query("wk_prod_rawkeyvalue"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(Auth.Domain.Errors.WebhookKeyErrors.Invalid);
        _repoMock.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(p => p.Publish(It.IsAny<Auth.Domain.Events.WebhookKeyHashUpgradedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData("", "WebhookKey.Required")]
    [InlineData("ak_prod_x", "WebhookKey.InvalidPrefix")]
    public async Task Handle_EmptyOrForeignPrefix_KeepsTodaysAnswers(string raw, string code)
    {
        var result = await _handler.Handle(Query(raw), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _repoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_CallerWithoutTheScope_ReturnsInsufficientScope_BeforeTheKeyIsRead()
    {
        var result = await _handler.Handle(
            Query("wk_prod_rawkeyvalue", Auth.Domain.Constants.MachineClients.Scopes.ApiKeysValidate), CancellationToken.None);

        result.FirstError.Code.Should().Be("Http.Forbidden");
        _repoMock.VerifyNoOtherCalls();
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
            _userRepositoryMock.Object,
            new Auth.Application.Security.Keyring.HmacKeyringHolder(Auth_API.Tests.Security.Keyring.KeyringTestRings.SingleKey()));
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

        _repoMock.Setup(r => r.ListAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdIncludingDeletedAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId, name: "Hooked App"));

        var result = await _handler.Handle(new GetWebhookKeysQuery(appId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(k => k.ApplicationName == "Hooked App");
    }

    [Fact]
    public async Task Handle_RevokedKey_CarriesTheReasonItWasRevokedWith()
    {
        // The row panel shows why a key was revoked; the reason is stored on the key and
        // must reach the listing, while a live key carries none.
        var appId = Guid.NewGuid();
        var revoked = TestHelpers.CreateWebhookKey(applicationId: appId, name: "Revoked");
        revoked.Revoke(Guid.NewGuid(), "Receiver decommissioned");
        var live = TestHelpers.CreateWebhookKey(applicationId: appId, name: "Live");

        _repoMock.Setup(r => r.ListAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookKey> { revoked, live });

        var result = await _handler.Handle(new GetWebhookKeysQuery(appId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Single(k => k.Name == "Revoked").RevokeReason.Should().Be("Receiver decommissioned");
        result.Value.Single(k => k.Name == "Revoked").RevokeReasonCode.Should().BeNull();
        result.Value.Single(k => k.Name == "Live").RevokeReason.Should().BeNull();
        result.Value.Single(k => k.Name == "Live").RevokeReasonCode.Should().BeNull();
    }

    [Theory]
    [InlineData("ApplicationDeleted")]
    [InlineData("Application deleted")] // written before Q6
    public async Task Handle_KeyOfADeletedApplication_CarriesTheReasonCode_NotProse(string stored)
    {
        // Q6: the console names the code in the reader's language; English prose would show in every one.
        var appId = Guid.NewGuid();
        var key = TestHelpers.CreateWebhookKey(applicationId: appId, name: "Orphaned");
        key.Revoke(Guid.NewGuid(), stored);

        _repoMock.Setup(r => r.ListAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookKey> { key });

        var dto = (await _handler.Handle(new GetWebhookKeysQuery(appId), CancellationToken.None)).Value.Single();

        dto.RevokeReasonCode.Should().Be("ApplicationDeleted");
        dto.RevokeReason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoApplicationId_ListsEveryApplicationsKeys()
    {
        var appOne = Guid.NewGuid();
        var appTwo = Guid.NewGuid();
        var keys = new List<WebhookKey>
        {
            TestHelpers.CreateWebhookKey(applicationId: appOne, name: "One"),
            TestHelpers.CreateWebhookKey(applicationId: appTwo, name: "Two")
        };

        _repoMock.Setup(r => r.ListAsync(null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);

        var result = await _handler.Handle(new GetWebhookKeysQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Select(k => k.ApplicationId).Should().BeEquivalentTo([appOne, appTwo]);
    }
}

/// <summary>
/// S11 trap 17: a webhook key still stored under an HMAC key that no longer issues is marked
/// for re-issue, with the date its old key's webhook grace ends; a key that validates
/// nothing any more (revoked or expired) is not.
/// </summary>
public class GetWebhookKeysReissueTests
{
    private static readonly DateTime Cutover = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Mac = "S2HEKqunY1YOsJmIBLV8BjHWXWzdVkpq/dDghqx3M20=";

    // Key 1 (the legacy form) and key 2 no longer issue; key 3 does.
    private static readonly HmacKeyring Ring = HmacKeyring.Create(
        [
            new HmacKey(1, Auth_API.Tests.Security.Keyring.KeyringTestRings.Key1Material, demotedAtUtc: Cutover),
            new HmacKey(2, Auth_API.Tests.Security.Keyring.KeyringTestRings.Key2Material, createdAtUtc: Cutover.AddDays(-1),
                demotedAtUtc: Cutover.AddDays(10), verifyUntilUtc: Cutover.AddDays(17)),
            new HmacKey(3, Auth_API.Tests.Security.Keyring.KeyringTestRings.Key3Material, createdAtUtc: Cutover.AddDays(9))
        ],
        currentKeyId: 3,
        nextKeyId: null,
        legacyCutoverUtc: Cutover,
        legacyVerifyUntilUtc: Cutover.AddDays(7)).Value;

    private static async Task<WebhookKeyDto> ListOneAsync(WebhookKey webhookKey)
    {
        var repository = new Mock<IWebhookKeyRepository>();
        repository
            .Setup(r => r.ListAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookKey> { webhookKey });
        var handler = new GetWebhookKeysQueryHandler(
            repository.Object, new Mock<IApplicationRepository>().Object, new Mock<IUserRepository>().Object, new HmacKeyringHolder(Ring));

        var result = await handler.Handle(new GetWebhookKeysQuery(), CancellationToken.None);
        return result.Value.Should().ContainSingle().Which;
    }

    [Fact]
    public async Task AKeyUnderAPreviousKey_NeedsReissue_ByItsWebhookGraceEnd()
    {
        var dto = await ListOneAsync(TestHelpers.CreateWebhookKey(keyHash: "hk2$" + Mac));

        dto.NeedsReissue.Should().BeTrue();
        dto.ReissueBy.Should().Be(Ring.AcceptedUntil(Ring.Find(2)!, CredentialPurpose.WebhookKey));
        dto.ReissueBy.Should().Be(Cutover.AddDays(10).AddDays(90), "a webhook key keeps 90 days from its key's demotion");
    }

    [Fact]
    public async Task AnUnprefixedKey_AfterTheCutover_NeedsReissue()
    {
        var dto = await ListOneAsync(TestHelpers.CreateWebhookKey(keyHash: Mac));

        dto.NeedsReissue.Should().BeTrue("the legacy form is key 1's, and key 1 no longer issues");
        dto.ReissueBy.Should().Be(Ring.AcceptedUntil(Ring.Find(1)!, CredentialPurpose.WebhookKey));
    }

    [Fact]
    public async Task AKeyUnderTheIssuingKey_DoesNotNeedReissue()
    {
        var dto = await ListOneAsync(TestHelpers.CreateWebhookKey(keyHash: "hk3$" + Mac));

        dto.NeedsReissue.Should().BeFalse();
        dto.ReissueBy.Should().BeNull();
    }

    [Fact]
    public async Task ARevokedKey_DoesNotNeedReissue()
    {
        var dto = await ListOneAsync(TestHelpers.CreateWebhookKey(
            keyHash: "hk2$" + Mac, revokedAt: DateTime.UtcNow.AddDays(-1), revokedBy: Guid.NewGuid()));

        dto.NeedsReissue.Should().BeFalse();
    }

    [Fact]
    public async Task AnExpiredKey_DoesNotNeedReissue()
    {
        var dto = await ListOneAsync(TestHelpers.CreateWebhookKey(keyHash: "hk2$" + Mac, expiresAt: DateTime.UtcNow.AddDays(-1)));

        dto.NeedsReissue.Should().BeFalse("an expired webhook key validates nothing, whatever key it is stored under");
    }
}
