using System.Text.Json;
using Auth.Application.Features.SystemSettings.GetSystemSettings;
using Auth.Application.Features.SystemSettings.ResetSystemSettings;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Administration;

/// <summary>
/// Shared plumbing for the system-settings handler tests: JSON payloads,
/// in-memory configuration, and startup snapshots captured from it.
/// </summary>
internal static class SystemSettingsTestSupport
{
    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static IConfiguration BuildConfiguration(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    /// <summary>
    /// A snapshot whose baseline and at-startup views both equal the given
    /// configuration (nothing pending, everything file-sourced).
    /// </summary>
    public static StartupValuesSnapshot SnapshotOf(IConfiguration configuration)
    {
        var captured = StartupValuesSnapshot.CaptureValues(configuration);
        return new StartupValuesSnapshot(captured, captured);
    }

    public static SystemSettingsOverride ExistingRow(
        string sectionKey,
        string overridesJson,
        byte[]? rowVersion = null,
        Guid? modifiedBy = null,
        int version = 1)
        => new(
            sectionKey,
            overridesJson,
            version,
            DateTime.UtcNow,
            modifiedBy ?? Guid.NewGuid(),
            rowVersion ?? [1, 2, 3, 4, 5, 6, 7, 8]);
}

public class UpdateSystemSettingsCommandHandlerTests
{
    private readonly Mock<ISystemSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ISystemSettingsReloader> _reloaderMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly UpdateSystemSettingsCommandHandler _handler;

    public UpdateSystemSettingsCommandHandlerTests()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("Jwt:Issuer", "https://auth.example.com"),
            ("Jwt:Audience", "https://api.example.com"),
            ("Password:MinimumLength", "10"),
            ("Password:HistoryCount", "5"),
            ("Gateway:ValidationEnabled", "true"),
            ("Gateway:ExemptPaths:0", "/health"),
            ("ImageStorage:PublicBaseUrl", "https://auth.example.com/uploads/images"),
            ("ImageStorage:RequestPath", "/uploads/images"));

        _handler = new UpdateSystemSettingsCommandHandler(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            configuration,
            SystemSettingsTestSupport.SnapshotOf(configuration),
            _reloaderMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<UpdateSystemSettingsCommandHandler>>().Object);
    }

    private static UpdateSystemSettingsCommand Command(
        string sectionKey,
        string overridesJson,
        string? rowVersion = null,
        Guid? updatedBy = null)
        => new(sectionKey, SystemSettingsTestSupport.Json(overridesJson), rowVersion, updatedBy ?? Guid.NewGuid());

    private void VerifyNothingWritten()
    {
        _settingsRepoMock.Verify(
            r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SystemSettingsUpdatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _reloaderMock.Verify(r => r.Reload(), Times.Never());
    }

    #region Whitelist and value validation

    [Fact]
    public async Task Handle_UnknownSection_ReturnsSectionNotFound()
    {
        var result = await _handler.Handle(Command("Nonsense", "{}"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.SectionNotFound");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_NonObjectPayload_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(Command("Password", "42"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_UnknownFieldPath_ReturnsUnknownField()
    {
        var result = await _handler.Handle(
            Command("Jwt", """{"Bogus":"value"}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.UnknownField");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_SensitiveField_ReturnsSecretManagedField()
    {
        var result = await _handler.Handle(
            Command("Jwt", """{"PrivateKeyPem":"-----BEGIN PRIVATE KEY-----"}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.SecretManagedField");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_ReadOnlyField_ReturnsUnknownField()
    {
        var result = await _handler.Handle(
            Command("Password", """{"SaltSize":32}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.UnknownField");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_IntBelowMinimum_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":4}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_EnumOutsideAllowedValues_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Password", """{"BreachedPasswordCheck":{"Mode":"Loud"}}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_BoolWithWrongJsonType_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Password", """{"RequireUppercase":"yes"}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_ArrayWithBlankEntry_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Gateway", """{"ExemptPaths":[""]}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_JwtIssuerNotAbsoluteUrl_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Jwt", """{"Issuer":"auth.example.com"}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_ExemptPathWithoutLeadingSlash_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("Gateway", """{"ExemptPaths":["health"]}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    // Image URLs are composed as PublicBaseUrl + '/' + file and served under
    // RequestPath. The two must agree, and only one of them takes effect
    // without a restart — so a save that breaks the pair is refused rather
    // than left to surface later as 404s on every logo.

    [Fact]
    public async Task Handle_PublicBaseUrlNotEndingWithServingPath_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("ImageStorage", """{"PublicBaseUrl":"https://cdn.example.com/media"}"""),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        result.FirstError.Description.Should().Contain("/uploads/images");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_RequestPathMovedWithoutPublicBaseUrl_ReturnsInvalidFieldValue()
    {
        // The mirror case: the effective base still points at the old path.
        var result = await _handler.Handle(
            Command("ImageStorage", """{"RequestPath":"/media"}"""), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_PublicBaseUrlWithoutAnyPath_ReturnsInvalidFieldValue()
    {
        var result = await _handler.Handle(
            Command("ImageStorage", """{"PublicBaseUrl":"https://cdn.example.com"}"""),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    [Theory]
    // Same host, different origin, rooted path, trailing slash, and a CDN that
    // prefixes the serving path — all keep the pairing intact.
    [InlineData("""{"PublicBaseUrl":"https://cdn.example.com/uploads/images"}""")]
    [InlineData("""{"PublicBaseUrl":"/uploads/images"}""")]
    [InlineData("""{"PublicBaseUrl":"https://cdn.example.com/uploads/images/"}""")]
    [InlineData("""{"PublicBaseUrl":"https://cdn.example.com/auth/uploads/images"}""")]
    [InlineData("""{"PublicBaseUrl":"https://cdn.example.com/media","RequestPath":"/media"}""")]
    public async Task Handle_PairingHeld_PassesValidation(string overridesJson)
    {
        _settingsRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));

        var result = await _handler.Handle(
            Command("ImageStorage", overridesJson), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _settingsRepoMock.Verify(
            r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_UnrelatedImageStorageField_IsNotPairChecked()
    {
        _settingsRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));

        var result = await _handler.Handle(
            Command("ImageStorage", """{"WebpQuality":80}"""), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MultipleInvalidFields_ReportsEveryErrorAtOnce()
    {
        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":4,"BreachedPasswordCheck":{"Mode":"Loud"}}"""),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().OnlyContain(e => e.Code == "SystemSettings.InvalidFieldValue");
        VerifyNothingWritten();
    }

    #endregion

    #region Optimistic concurrency

    [Fact]
    public async Task Handle_ExistingRowButNullRowVersion_ReturnsConcurrencyConflictWithoutWriting()
    {
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("Password", """{"MinimumLength":12}"""));

        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":14}""", rowVersion: null), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ConcurrencyConflict");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_RowVersionProvidedButRowAbsent_ReturnsConcurrencyConflict()
    {
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSettingsOverride?)null);

        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":14}""", rowVersion: Convert.ToBase64String([9, 9, 9])),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ConcurrencyConflict");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_UpsertReportsConflict_ReturnsConcurrencyConflict()
    {
        var storedRowVersion = new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 };
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("Password", """{"MinimumLength":12}""", storedRowVersion));
        _settingsRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(false, null, null));

        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":14}""", rowVersion: Convert.ToBase64String(storedRowVersion)),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ConcurrencyConflict");
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SystemSettingsUpdatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _reloaderMock.Verify(r => r.Reload(), Times.Never());
    }

    [Fact]
    public async Task Handle_InvalidBase64RowVersion_ReturnsConcurrencyConflict()
    {
        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":14}""", rowVersion: "!!!not-base64!!!"),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ConcurrencyConflict");
        _settingsRepoMock.Verify(
            r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        VerifyNothingWritten();
    }

    #endregion

    #region Successful save

    [Fact]
    public async Task Handle_ValidUpdateNoExistingRow_PersistsPublishesReloadsAndProjectsOverride()
    {
        var updatedBy = Guid.NewGuid();
        var newRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSettingsOverride?)null);
        _settingsRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, newRowVersion, 1));
        _userRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { TestHelpers.CreateUser(id: updatedBy, firstName: "Settings", lastName: "Admin") });

        var result = await _handler.Handle(
            Command("Password", """{"MinimumLength":12}""", rowVersion: null, updatedBy: updatedBy),
            CancellationToken.None);

        result.IsError.Should().BeFalse();

        _settingsRepoMock.Verify(
            r => r.UpsertAsync(
                It.Is<SystemSettingsOverride>(o =>
                    o.SectionKey == "Password" &&
                    o.OverridesJson == """{"MinimumLength":12}""" &&
                    o.ModifiedBy == updatedBy),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once());

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SystemSettingsUpdatedEvent>(e =>
                    e.SectionKey == "Password" &&
                    e.OldOverridesJson == "{}" &&
                    e.NewOverridesJson == """{"MinimumLength":12}""" &&
                    e.UpdatedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());

        _reloaderMock.Verify(r => r.Reload(), Times.Once());

        var dto = result.Value;
        dto.Key.Should().Be("Password");
        dto.Version.Should().Be(1);
        dto.RowVersion.Should().Be(Convert.ToBase64String(newRowVersion));
        dto.ModifiedByName.Should().Be("Settings Admin");

        var minimumLength = dto.Fields.Single(f => f.Path == "MinimumLength");
        minimumLength.OverrideValue.Should().Be(12L);
        minimumLength.Source.Should().Be("database");
    }

    [Fact]
    public async Task Handle_ValidUpdateExistingRow_PassesExpectedRowVersionAndPublishesOldJson()
    {
        var updatedBy = Guid.NewGuid();
        var storedRowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        var newRowVersion = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
        const string oldJson = """{"ValidationEnabled":false}""";

        _settingsRepoMock
            .Setup(r => r.GetAsync("Gateway", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("Gateway", oldJson, storedRowVersion));
        _settingsRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, newRowVersion, 2));

        var result = await _handler.Handle(
            Command("Gateway", """{"ValidationEnabled":true}""", Convert.ToBase64String(storedRowVersion), updatedBy),
            CancellationToken.None);

        result.IsError.Should().BeFalse();

        _settingsRepoMock.Verify(
            r => r.UpsertAsync(
                It.IsAny<SystemSettingsOverride>(),
                It.Is<byte[]?>(b => b != null && b.SequenceEqual(storedRowVersion)),
                It.IsAny<CancellationToken>()),
            Times.Once());

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SystemSettingsUpdatedEvent>(e =>
                    e.SectionKey == "Gateway" &&
                    e.OldOverridesJson == oldJson &&
                    e.NewOverridesJson == """{"ValidationEnabled":true}"""),
                It.IsAny<CancellationToken>()),
            Times.Once());

        _reloaderMock.Verify(r => r.Reload(), Times.Once());

        result.Value.Version.Should().Be(2);
        result.Value.RowVersion.Should().Be(Convert.ToBase64String(newRowVersion));
        result.Value.Fields.Single(f => f.Path == "ValidationEnabled").OverrideValue.Should().Be(true);
    }

    #endregion
}

public class ResetSystemSettingsCommandHandlerTests
{
    private readonly Mock<ISystemSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<ISystemSettingsReloader> _reloaderMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly ResetSystemSettingsCommandHandler _handler;

    public ResetSystemSettingsCommandHandlerTests()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("Password:MinimumLength", "10"),
            ("Password:HistoryCount", "5"));

        _handler = new ResetSystemSettingsCommandHandler(
            _settingsRepoMock.Object,
            configuration,
            SystemSettingsTestSupport.SnapshotOf(configuration),
            _reloaderMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<ResetSystemSettingsCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_UnknownSection_ReturnsSectionNotFound()
    {
        var result = await _handler.Handle(
            new ResetSystemSettingsCommand("Nonsense", Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.SectionNotFound");
    }

    [Fact]
    public async Task Handle_ExistingRow_DeletesPublishesReloadsAndReturnsCleanDto()
    {
        var updatedBy = Guid.NewGuid();
        const string oldJson = """{"MinimumLength":12}""";
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("Password", oldJson));
        _settingsRepoMock
            .Setup(r => r.DeleteAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new ResetSystemSettingsCommand("Password", updatedBy), CancellationToken.None);

        result.IsError.Should().BeFalse();

        _settingsRepoMock.Verify(r => r.DeleteAsync("Password", It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SystemSettingsUpdatedEvent>(e =>
                    e.SectionKey == "Password" &&
                    e.OldOverridesJson == oldJson &&
                    e.NewOverridesJson == "{}" &&
                    e.UpdatedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());
        _reloaderMock.Verify(r => r.Reload(), Times.Once());

        var dto = result.Value;
        dto.Key.Should().Be("Password");
        dto.Version.Should().Be(0);
        dto.RowVersion.Should().BeNull();

        var minimumLength = dto.Fields.Single(f => f.Path == "MinimumLength");
        minimumLength.OverrideValue.Should().BeNull();
        minimumLength.Source.Should().Be("file");
    }

    [Fact]
    public async Task Handle_NoRow_DeletesNothingPublishesNothingAndStillReturnsDto()
    {
        _settingsRepoMock
            .Setup(r => r.GetAsync("Password", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSettingsOverride?)null);

        var result = await _handler.Handle(
            new ResetSystemSettingsCommand("Password", Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Key.Should().Be("Password");
        result.Value.Version.Should().Be(0);

        _settingsRepoMock.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SystemSettingsUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
        _reloaderMock.Verify(r => r.Reload(), Times.Never());
    }
}

public class GetSystemSettingsQueryHandlerTests
{
    private readonly Mock<ISystemSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ISystemSettingsReloader> _reloaderMock = new();

    private GetSystemSettingsQueryHandler CreateHandler(IConfiguration configuration, IStartupValuesSnapshot snapshot)
        => new(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            configuration,
            snapshot,
            _reloaderMock.Object);

    [Fact]
    public async Task Handle_ReturnsEveryRegistrySectionInOrder()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration();
        _settingsRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemSettingsOverride>());

        var result = await CreateHandler(configuration, SystemSettingsTestSupport.SnapshotOf(configuration))
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Sections.Select(s => s.Key)
            .Should().Equal(SystemSettingsRegistry.Sections.Select(d => d.Key));

        // Sensitive fields are projected as facts, never as values.
        var privateKeyPem = result.Value.Sections.Single(s => s.Key == "Jwt")
            .Fields.Single(f => f.Path == "PrivateKeyPem");
        privateKeyPem.Sensitive.Should().BeTrue();
        privateKeyPem.ReadOnly.Should().BeTrue();
        privateKeyPem.Source.Should().Be("secrets");
        privateKeyPem.EffectiveValue.Should().BeNull();
        privateKeyPem.OverrideValue.Should().BeNull();
        privateKeyPem.BaselineValue.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RestartRequiredFieldChangedSinceStartup_SetsRestartPending()
    {
        var startupConfig = SystemSettingsTestSupport.BuildConfiguration(
            ("Jwt:Issuer", "https://old.example.com"),
            ("Password:MinimumLength", "10"));
        var currentConfig = SystemSettingsTestSupport.BuildConfiguration(
            ("Jwt:Issuer", "https://new.example.com"),
            ("Password:MinimumLength", "12"));
        var captured = StartupValuesSnapshot.CaptureValues(startupConfig);
        var snapshot = new StartupValuesSnapshot(captured, captured);

        _settingsRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemSettingsOverride>());

        var result = await CreateHandler(currentConfig, snapshot)
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RestartPending.Should().BeTrue();

        var issuer = result.Value.Sections.Single(s => s.Key == "Jwt")
            .Fields.Single(f => f.Path == "Issuer");
        issuer.RestartRequired.Should().BeTrue();
        issuer.IsPendingRestart.Should().BeTrue();

        // A hot (non-restart) field changing must never flag a pending restart.
        var minimumLength = result.Value.Sections.Single(s => s.Key == "Password")
            .Fields.Single(f => f.Path == "MinimumLength");
        minimumLength.RestartRequired.Should().BeFalse();
        minimumLength.IsPendingRestart.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AttributesFieldSourcesToDatabaseFileAndDefault()
    {
        var modifier = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        // Running configuration already includes the database layer.
        var currentConfig = SystemSettingsTestSupport.BuildConfiguration(
            ("Password:HistoryCount", "5"),
            ("Password:MinimumLength", "12"));
        var baselineConfig = SystemSettingsTestSupport.BuildConfiguration(
            ("Password:HistoryCount", "5"));
        var snapshot = new StartupValuesSnapshot(
            StartupValuesSnapshot.CaptureValues(baselineConfig),
            StartupValuesSnapshot.CaptureValues(currentConfig));

        _settingsRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemSettingsOverride>
            {
                SystemSettingsTestSupport.ExistingRow(
                    "Password", """{"MinimumLength":12}""", rowVersion, modifier, version: 3)
            });
        _userRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { TestHelpers.CreateUser(id: modifier, firstName: "Settings", lastName: "Admin") });

        var result = await CreateHandler(currentConfig, snapshot)
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RestartPending.Should().BeFalse();

        var password = result.Value.Sections.Single(s => s.Key == "Password");
        password.Version.Should().Be(3);
        password.RowVersion.Should().Be(Convert.ToBase64String(rowVersion));
        password.ModifiedByName.Should().Be("Settings Admin");

        var passwordDefinition = SystemSettingsRegistry.TryGet("Password")!;

        var minimumLength = password.Fields.Single(f => f.Path == "MinimumLength");
        minimumLength.Source.Should().Be("database");
        minimumLength.OverrideValue.Should().Be(12L);
        minimumLength.EffectiveValue.Should().Be(12L);
        minimumLength.BaselineValue.Should().Be(
            SystemSettingsRegistry.TryGetField(passwordDefinition, "MinimumLength")!.DefaultValue,
            "an unset baseline falls back to the registry's settings-class default");

        var historyCount = password.Fields.Single(f => f.Path == "HistoryCount");
        historyCount.Source.Should().Be("file");
        historyCount.OverrideValue.Should().BeNull();
        historyCount.BaselineValue.Should().Be(5L);

        var lockoutDuration = password.Fields.Single(f => f.Path == "LockoutDurationMinutes");
        lockoutDuration.Source.Should().Be("default");
        lockoutDuration.EffectiveValue.Should().Be(
            SystemSettingsRegistry.TryGetField(passwordDefinition, "LockoutDurationMinutes")!.DefaultValue,
            "a field neither files nor database configure still reports the running class default");
    }

    [Fact]
    public async Task Handle_RepositoryThrows_FailsOpenWithRegistrySectionsAndNoOverrideData()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("Password:MinimumLength", "10"));
        _settingsRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await CreateHandler(configuration, SystemSettingsTestSupport.SnapshotOf(configuration))
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Sections.Should().HaveCount(SystemSettingsRegistry.Sections.Count);
        result.Value.Sections.Should().OnlyContain(s => s.Version == 0 && s.RowVersion == null);
        result.Value.Sections.SelectMany(s => s.Fields)
            .Should().OnlyContain(f => f.OverrideValue == null && f.Source != "database");
    }

    [Fact]
    public async Task Handle_ReloaderReportsFailedLoad_SetsDbOverridesUnavailable()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration();
        _settingsRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemSettingsOverride>());
        _reloaderMock.Setup(r => r.LastLoadFailed).Returns(true);

        var result = await CreateHandler(configuration, SystemSettingsTestSupport.SnapshotOf(configuration))
            .Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.DbOverridesUnavailable.Should().BeTrue();
    }
}
