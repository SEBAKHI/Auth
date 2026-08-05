using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.GetNotificationOutboxMessageById;
using Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;
using Auth.Application.Features.PrivacyPolicy.NotifyPrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.SavePrivacyPolicyContent;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// Unit tests for the privacy-policy revision registry (create + notify) and
/// the delivery-log redaction of sensitive rendered bodies.
/// </summary>
public class PrivacyPolicyTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    #region CreatePrivacyPolicyVersion

    [Fact]
    public async Task Create_NewVersion_ReturnsDto()
    {
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.TryCreateAsync(It.IsAny<PrivacyPolicyVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new CreatePrivacyPolicyVersionCommandHandler(repository.Object);

        var result = await handler.Handle(
            new CreatePrivacyPolicyVersionCommand("2026.09", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "Initial")
            {
                RequestedBy = AdminId
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Version.Should().Be("2026.09");
        result.Value.NotifiedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Create_DuplicateVersion_ReturnsConflict()
    {
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.TryCreateAsync(It.IsAny<PrivacyPolicyVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = new CreatePrivacyPolicyVersionCommandHandler(repository.Object);

        var result = await handler.Handle(
            new CreatePrivacyPolicyVersionCommand("2026.07", DateTime.UtcNow, null) { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.DuplicateVersion");
    }

    #endregion

    #region NotifyPrivacyPolicyVersion

    private static (NotifyPrivacyPolicyVersionCommandHandler Handler,
        Mock<IPrivacyPolicyVersionRepository> Versions,
        Mock<IUserRepository> Users,
        Mock<INotificationService> Notifications,
        Mock<IAuditLogRepository> Audit) CreateNotifyHandler(PrivacyPolicyVersion? version)
    {
        var versions = new Mock<IPrivacyPolicyVersionRepository>();
        versions
            .Setup(r => r.GetByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        var users = new Mock<IUserRepository>();
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        var audit = new Mock<IAuditLogRepository>();

        var handler = new NotifyPrivacyPolicyVersionCommandHandler(
            versions.Object,
            users.Object,
            notifications.Object,
            audit.Object,
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://accounts.example.com" }),
            new Mock<ILogger<NotifyPrivacyPolicyVersionCommandHandler>>().Object);

        return (handler, versions, users, notifications, audit);
    }

    [Fact]
    public async Task Notify_UnknownVersion_ReturnsNotFound()
    {
        var (handler, _, _, notifications, _) = CreateNotifyHandler(version: null);

        var result = await handler.Handle(
            new NotifyPrivacyPolicyVersionCommand("2030.01") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.NotFound");
        notifications.Verify(
            n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Notify_SendsToEveryActiveUser_AndStampsVersion()
    {
        var version = PrivacyPolicyVersion.Create(
            "2026.07", new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc), null, AdminId);
        var (handler, versions, users, notifications, audit) = CreateNotifyHandler(version);

        var alice = (Id: Guid.NewGuid(), Email: "alice@example.com",
            DisplayName: (string?)"Alice", FirstName: (string?)"Alice", PreferredLanguage: (string?)"en");
        var bora = (Id: Guid.NewGuid(), Email: "bora@example.com",
            DisplayName: (string?)null, FirstName: (string?)"Bora", PreferredLanguage: (string?)"tr");
        users
            .Setup(r => r.GetActiveNotificationRecipientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([alice, bora]);

        var result = await handler.Handle(
            new NotifyPrivacyPolicyVersionCommand("2026.07") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecipientCount.Should().Be(2);

        notifications.Verify(n => n.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.PrivacyPolicyUpdated &&
                r.RecipientAddress == "alice@example.com" &&
                r.RecipientUserId == alice.Id &&
                Equals(r.Variables!["PolicyVersion"], "2026.07") &&
                Equals(r.Variables!["PolicyLink"], "https://accounts.example.com/privacy")),
            It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendAsync(
            It.Is<NotificationRequest>(r => r.RecipientAddress == "bora@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);

        version.NotifiedAtUtc.Should().NotBeNull();
        version.NotifiedCount.Should().Be(2);
        versions.Verify(r => r.UpdateNotifiedAsync(version, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.CreateAsync(
            It.Is<AuditLog>(log => log.Action == "system.policy_notification_sent"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_FailedSends_AreSkippedNotCounted()
    {
        var version = PrivacyPolicyVersion.Create("2026.07", DateTime.UtcNow, null, AdminId);
        var (handler, _, users, notifications, _) = CreateNotifyHandler(version);

        var ok = (Id: Guid.NewGuid(), Email: "ok@example.com",
            DisplayName: (string?)"Ok", FirstName: (string?)"Ok", PreferredLanguage: (string?)"en");
        var bad = (Id: Guid.NewGuid(), Email: "bad@example.com",
            DisplayName: (string?)"Bad", FirstName: (string?)"Bad", PreferredLanguage: (string?)"en");
        users
            .Setup(r => r.GetActiveNotificationRecipientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ok, bad]);
        notifications
            .Setup(n => n.SendAsync(
                It.Is<NotificationRequest>(r => r.RecipientAddress == "bad@example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Auth.Domain.Errors.NotificationErrors.SendFailed);

        var result = await handler.Handle(
            new NotifyPrivacyPolicyVersionCommand("2026.07") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecipientCount.Should().Be(1);
        version.NotifiedCount.Should().Be(1);
    }

    #endregion

    #region Published policy + live disclosure

    private static PrivacyPolicyVersion PublishedVersion()
    {
        return new PrivacyPolicyVersion(
            Guid.NewGuid(), "2026.07", new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
            isPublished: true, changeNote: null, notifiedAtUtc: null, notifiedCount: null,
            createdAt: DateTime.UtcNow, createdBy: AdminId);
    }

    private static AccountDeletionSettings Settings() => new()
    {
        GraceDays = 30,
        OtpExpirationMinutes = 15,
        LoginAttemptRetentionDays = 365,
        OutboxRetentionDays = 180,
        PolicyVersion = "2026.07"
    };

    /// <summary>A fully identified controller — the publishable state.</summary>
    private static DataControllerSettings Controller() => new()
    {
        LegalName = "Acme Corp LLC",
        Address = "1 Example Street, Istanbul",
        PrivacyEmail = "privacy@example.com",
        EmailProvider = "Example Mail",
        HostingProvider = "Example Hosting",
        HostingCountry = "Türkiye"
    };

    [Fact]
    public async Task GetPublished_ReturnsDocumentWithLiveConfigurationValues()
    {
        // The whole point of the token design: changing appsettings changes
        // the published policy without touching content.
        var version = PublishedVersion();
        var settings = Settings();
        settings.OtpExpirationMinutes = 7;
        settings.GraceDays = 45;

        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetPublishedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "tr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivacyPolicyTranslation.Create(
                version.Id, "tr", "{\"title\":\"Gizlilik\"}", AdminId));

        var handler = new GetPublishedPrivacyPolicyQueryHandler(
            repository.Object, TestHelpers.CreateOptions(settings), TestHelpers.CreateOptions(Controller()));

        var result = await handler.Handle(
            new GetPublishedPrivacyPolicyQuery("tr-TR"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("tr");
        result.Value.Version.Should().Be("2026.07");
        result.Value.Disclosure.OtpValidityMinutes.Should().Be(7);
        result.Value.Disclosure.GraceDays.Should().Be(45);
        result.Value.Disclosure.LoginAttemptRetentionDays.Should().Be(365);
    }

    [Fact]
    public async Task GetPublished_UnwrittenLanguage_FallsBackToNeutral()
    {
        var version = PublishedVersion();
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetPublishedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "fr", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrivacyPolicyTranslation?)null);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivacyPolicyTranslation.Create(
                version.Id, "en", "{\"title\":\"Privacy\"}", AdminId));

        var handler = new GetPublishedPrivacyPolicyQueryHandler(
            repository.Object, TestHelpers.CreateOptions(Settings()), TestHelpers.CreateOptions(Controller()));

        var result = await handler.Handle(
            new GetPublishedPrivacyPolicyQuery("fr"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("en");
    }

    [Fact]
    public async Task GetPublished_NothingPublished_ReturnsNotFound()
    {
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetPublishedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrivacyPolicyVersion?)null);

        var handler = new GetPublishedPrivacyPolicyQueryHandler(
            repository.Object, TestHelpers.CreateOptions(Settings()), TestHelpers.CreateOptions(Controller()));

        var result = await handler.Handle(
            new GetPublishedPrivacyPolicyQuery(null), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.NoPublishedVersion");
    }

    #endregion

    #region Content editing

    private static (SavePrivacyPolicyContentCommandHandler Handler,
        Mock<IPrivacyPolicyVersionRepository> Repository) CreateSaveHandler(
        PrivacyPolicyVersion? version)
    {
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        var handler = new SavePrivacyPolicyContentCommandHandler(
            repository.Object, new Mock<IAuditLogRepository>().Object);
        return (handler, repository);
    }

    /// <summary>A document carrying every member the renderer requires.</summary>
    private static string ValidDocument() =>
        """
        {"title":"T","effectiveDate":"D","versionLabel":"V","intro":[],
         "sections":[],"retention":{},"deletion":{},"rights":[],"closing":[]}
        """;

    [Fact]
    public async Task SaveContent_ValidDocument_Upserts()
    {
        var version = PublishedVersion();
        var (handler, repository) = CreateSaveHandler(version);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrivacyPolicyTranslation?)null);

        var result = await handler.Handle(
            new SavePrivacyPolicyContentCommand("2026.07", "ar", ValidDocument())
            {
                RequestedBy = AdminId
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LanguageCode.Should().Be("ar");
        repository.Verify(r => r.UpsertTranslationAsync(
            It.Is<PrivacyPolicyTranslation>(t => t.LanguageCode == "ar"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("not json at all", "not valid JSON")]
    [InlineData("{\"title\":\"only\"}", "missing required section")]
    [InlineData("", "content is empty")]
    public async Task SaveContent_MalformedDocument_IsRejected(string content, string expected)
    {
        // A bad save would break the public page for everyone in that
        // language, so validation happens before storage.
        var (handler, repository) = CreateSaveHandler(PublishedVersion());

        var result = await handler.Handle(
            new SavePrivacyPolicyContentCommand("2026.07", "en", content) { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        result.FirstError.Description.Should().Contain(expected);
        repository.Verify(r => r.UpsertTranslationAsync(
            It.IsAny<PrivacyPolicyTranslation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveContent_UnsupportedLanguage_IsRejected()
    {
        var (handler, _) = CreateSaveHandler(PublishedVersion());

        var result = await handler.Handle(
            new SavePrivacyPolicyContentCommand("2026.07", "de", ValidDocument())
            {
                RequestedBy = AdminId
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.UnsupportedLanguage");
    }

    [Fact]
    public async Task Publish_WithoutNeutralDocument_IsRejected()
    {
        // Publishing without the fallback language would leave visitors whose
        // language is unwritten with nothing to read.
        var version = PublishedVersion();
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetByVersionAsync("2026.09", It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrivacyPolicyTranslation?)null);

        var handler = new PublishPrivacyPolicyVersionCommandHandler(
            repository.Object, new Mock<IAuditLogRepository>().Object, TestHelpers.CreateOptions(Controller()));

        var result = await handler.Handle(
            new PublishPrivacyPolicyVersionCommand("2026.09") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        repository.Verify(
            r => r.PublishAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_WithNeutralDocument_Publishes()
    {
        var version = PublishedVersion();
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetByVersionAsync("2026.09", It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        repository
            .Setup(r => r.GetTranslationAsync(version.Id, "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivacyPolicyTranslation.Create(version.Id, "en", "{}", AdminId));

        var audit = new Mock<IAuditLogRepository>();
        var handler = new PublishPrivacyPolicyVersionCommandHandler(
            repository.Object, audit.Object, TestHelpers.CreateOptions(Controller()));

        var result = await handler.Handle(
            new PublishPrivacyPolicyVersionCommand("2026.09") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        repository.Verify(r => r.PublishAsync(version.Id, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.CreateAsync(
            It.Is<AuditLog>(log => log.Action == "system.privacy_policy_published"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Delivery-log redaction

    private static NotificationOutboxMessage CreateOutboxMessage(string typeCode)
    {
        return new NotificationOutboxMessage(
            Guid.NewGuid(), typeCode, NotificationChannelType.Email,
            null, "user@example.com", "Jane", Guid.NewGuid(), "en",
            Guid.NewGuid(), Guid.NewGuid(), 2, "Subject", "<p>secret 123456</p>", "secret 123456",
            NotificationDeliveryStatus.Pending, 0,
            DateTime.UtcNow, DateTime.UtcNow, null, null, DateTime.UtcNow, null);
    }

    [Fact]
    public async Task OutboxDetail_SensitiveType_RedactsBodiesInEveryStatus()
    {
        // Pending on purpose: the at-rest redaction only covers Sent rows, so
        // the read model must hide sensitive bodies for the rest.
        var message = CreateOutboxMessage(NotificationTypeCodes.EmailVerification);
        var outbox = new Mock<INotificationOutboxRepository>();
        outbox
            .Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        var handler = new GetNotificationOutboxMessageByIdQueryHandler(
            outbox.Object, new Mock<IApplicationRepository>().Object);

        var result = await handler.Handle(
            new GetNotificationOutboxMessageByIdQuery(message.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.BodyHtml.Should().Be(NotificationTypeCodes.RedactedBody);
        result.Value.BodyText.Should().Be(NotificationTypeCodes.RedactedBody);
        result.Value.Subject.Should().Be("Subject");
    }

    [Fact]
    public async Task OutboxDetail_NonSensitiveType_ReturnsBodies()
    {
        var message = CreateOutboxMessage(NotificationTypeCodes.AccountDeletionRequested);
        var outbox = new Mock<INotificationOutboxRepository>();
        outbox
            .Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        var handler = new GetNotificationOutboxMessageByIdQueryHandler(
            outbox.Object, new Mock<IApplicationRepository>().Object);

        var result = await handler.Handle(
            new GetNotificationOutboxMessageByIdQuery(message.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.BodyHtml.Should().Be("<p>secret 123456</p>");
    }

    #endregion
}
