using Auth.Application.Features.WebhookKeys.ValidateWebhookKey;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Modules.WebhookKeyManagement.EventHandlers;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.WebhookKeyManagement;

/// <summary>
/// S11 TS6 (D-T3, owner decision §11 q2): a webhook key that validates under an HMAC key
/// that no longer issues is re-fingerprinted under the issuing key, so it keeps working
/// past the old key's 90-day grace. The rewrite is conditional on the stored value the
/// validation matched, and nothing it does (no row, an error) can fail the validation.
/// </summary>
public class WebhookKeyHashUpgradedEventHandlerTests
{
    private static readonly Guid KeyId = Guid.NewGuid();

    [Fact]
    public async Task TheRewrite_IsConditionalOnTheMatchedFingerprint()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var upgraded = await new WebhookKeyRepository(db).UpgradeKeyHashAsync(KeyId, "old-form", "hk2$new-form", CancellationToken.None);

        upgraded.Should().BeTrue();
        db.LastCommand!.CommandText.Should().Contain("SET [KeyHash] = @NewHash")
            .And.Contain("WHERE [Id] = @Id AND [KeyHash] = @MatchedHash",
                "a concurrent re-fingerprint, a rotation or a revoke that got there first must win");
        db.LastCommand.Parameters["MatchedHash"].Should().Be("old-form");
        db.LastCommand.Parameters["NewHash"].Should().Be("hk2$new-form");
    }

    [Fact]
    public async Task ARowThatChangedFirst_IsLeftAlone()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var upgraded = await new WebhookKeyRepository(db).UpgradeKeyHashAsync(KeyId, "old-form", "hk2$new-form", CancellationToken.None);

        upgraded.Should().BeFalse();
    }

    [Fact]
    public async Task TheHandler_RewritesThroughTheRepository_WithTheEventsValues()
    {
        var repository = new Mock<IWebhookKeyRepository>();
        repository
            .Setup(r => r.UpgradeKeyHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Handler(repository).Handle(new WebhookKeyHashUpgradedEvent(KeyId, "old-form", "hk2$new-form"), CancellationToken.None);

        repository.Verify(r => r.UpgradeKeyHashAsync(KeyId, "old-form", "hk2$new-form", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailure_IsLoggedAndSwallowed_SoTheValidationThatSucceededStands()
    {
        var repository = new Mock<IWebhookKeyRepository>();
        repository
            .Setup(r => r.UpgradeKeyHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the database is unreachable"));
        var logger = new Mock<ILogger<WebhookKeyHashUpgradedEventHandler>>();

        var handle = () => Handler(repository, logger).Handle(
            new WebhookKeyHashUpgradedEvent(KeyId, "old-form", "hk2$new-form"), CancellationToken.None);

        await handle.Should().NotThrowAsync();
        logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AValidation_UnderAnOldKey_AsksForTheRewrite()
    {
        var (handler, publisher) = ValidateHandler(storedHash: "legacy-form");

        var result = await handler.Handle(new ValidateWebhookKeyQuery("wk_prod_raw"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        publisher.Verify(
            p => p.Publish(
                It.Is<WebhookKeyHashUpgradedEvent>(e => e.MatchedHash == "legacy-form" && e.NewHash == "issuing-form"),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task AValidation_UnderTheIssuingKey_WritesNothing()
    {
        var (handler, publisher) = ValidateHandler(storedHash: "issuing-form");

        var result = await handler.Handle(new ValidateWebhookKeyQuery("wk_prod_raw"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        publisher.Verify(p => p.Publish(It.IsAny<WebhookKeyHashUpgradedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static WebhookKeyHashUpgradedEventHandler Handler(
        Mock<IWebhookKeyRepository> repository, Mock<ILogger<WebhookKeyHashUpgradedEventHandler>>? logger = null) =>
        new(repository.Object, (logger ?? new Mock<ILogger<WebhookKeyHashUpgradedEventHandler>>()).Object);

    private static (ValidateWebhookKeyQueryHandler Handler, Mock<IPublisher> Publisher) ValidateHandler(string storedHash)
    {
        var hasher = new FakeCredentialHasher().Map("wk_prod_raw", "issuing-form");
        var repository = new Mock<IWebhookKeyRepository>();
        repository
            .Setup(r => r.GetByHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateWebhookKey(keyHash: storedHash));
        var publisher = new Mock<IPublisher>();

        return (new ValidateWebhookKeyQueryHandler(
            repository.Object,
            hasher,
            publisher.Object,
            new Mock<ILogger<ValidateWebhookKeyQueryHandler>>().Object), publisher);
    }
}
