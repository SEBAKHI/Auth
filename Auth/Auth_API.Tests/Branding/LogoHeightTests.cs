using System.Text.RegularExpressions;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Application.Features.Applications.UpdateApplicationLogoHeight;
using Auth.Application.Features.Platform.UpdatePlatformLogoHeight;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationEntity = Auth.Domain.Entities.Application;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.Branding;

/// <summary>
/// The height a logo is shown at on the sign-in pages (UI batch P3, owner decisions D5/D6): one
/// stored height per logo owner, 24–120 px, null meaning the owner's default (60 px for the
/// platform, 48 px for an application). It is the area the logo takes on the page, saved on its
/// own endpoint without touching the image or the rest of the branding.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning tests here red: widen the range in <c>LogoHeight.CreateOptional</c>
/// (the 23/121 cases); drop the <c>Equals</c> short-cut in either <c>SetLogoHeight</c> (an
/// unchanged save writes and publishes); leave <c>LogoHeightPx</c> out of <c>PlatformSettingsDto.From</c>
/// or the public branding (the answers lose the height); write the height from
/// <c>UpdateAsync</c> (the statement count).
/// </remarks>
public class LogoHeightTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    // --- the value ---

    [Theory]
    [InlineData(24)]
    [InlineData(60)]
    [InlineData(120)]
    public void AHeightInsideTheRange_IsKept(int px)
    {
        var height = LogoHeight.CreateOptional(px);

        height.IsError.Should().BeFalse();
        height.Value!.Px.Should().Be(px);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(121)]
    [InlineData(0)]
    [InlineData(-60)]
    public void AHeightOutsideTheRange_IsRefusedWithThePublishedCode(int px)
    {
        var height = LogoHeight.CreateOptional(px);

        height.IsError.Should().BeTrue();
        height.FirstError.Code.Should().Be("LogoHeight.OutOfRange");
        height.FirstError.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void NoHeight_MeansTheDefault()
    {
        var height = LogoHeight.CreateOptional(null);

        height.IsError.Should().BeFalse();
        height.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(23, null)]
    [InlineData(121, null)]
    [InlineData(80, 80)]
    public void AStoredHeight_OutsideTheRange_ReadsAsTheDefault(int? stored, int? read)
    {
        LogoHeight.FromStored(stored)?.Px.Should().Be(read);
        (LogoHeight.FromStored(stored) is null).Should().Be(read is null);
    }

    [Fact]
    public void TheDefaults_AreTheOwnersDecision()
    {
        // D-SIZE: 1.5 × the platform's former 40px. D5: an application's stays at its 48px.
        LogoHeights.PlatformDefaultPx.Should().Be(60);
        LogoHeights.ApplicationDefaultPx.Should().Be(48);
        LogoHeights.MinPx.Should().Be(24);
        LogoHeights.MaxPx.Should().Be(120);
    }

    // --- the schema ---

    private static string TableSource(string table) =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                $"{table}.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    [Theory]
    [InlineData("PlatformSettings")]
    [InlineData("Applications")]
    public void TheColumn_IsAppendedLast_NullableWithoutADefault(string table)
    {
        var columns = OrganizationCreationSchemaTests.DeclaredColumns(TableSource(table), table);
        // Columns a later batch appended after this one (the platform's fonts: PlatformFontsTests).
        var later = table == "PlatformSettings" ? 4 : 0;

        columns.Should().NotBeEmpty();
        var column = columns[^(1 + later)];
        column.Name.Should().Be("LogoHeightPx", "appended, the publish is an ALTER TABLE ADD, not a rebuild");
        column.Definition.Should().MatchRegex(@"^INT\s+NULL$");
    }

    [Theory]
    [InlineData("PlatformSettings")]
    [InlineData("Applications")]
    public void TheColumn_IsCheckedToTheDomainsRange_UnderANamedConstraint(string table)
    {
        var match = Regex.Match(
            TableSource(table),
            $@"CONSTRAINT \[CK_{table}_LogoHeightPx\] CHECK \(\[LogoHeightPx\] IS NULL OR \(\[LogoHeightPx\] >= (\d+) AND \[LogoHeightPx\] <= (\d+)\)\)");

        match.Success.Should().BeTrue();
        int.Parse(match.Groups[1].Value).Should().Be(LogoHeights.MinPx);
        int.Parse(match.Groups[2].Value).Should().Be(LogoHeights.MaxPx);
    }

    [Theory]
    [InlineData("ApplicationRepository.cs")]
    [InlineData("PlatformSettingsRepository.cs")]
    public void OnlyTheDedicatedStatement_WritesTheHeight(string file)
    {
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", file));

        Regex.Matches(source, @"\[LogoHeightPx\] = @LogoHeightPx").Should().ContainSingle(
            "a full save built from a stale copy must never put back an older height");
    }

    // --- the platform's height ---

    private sealed class PlatformHarness
    {
        public Mock<IPlatformSettingsRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<PlatformLogoHeightChangedEvent> Published { get; } = [];
        public List<string> Order { get; } = [];
        public PlatformSettings Settings { get; }

        public PlatformHarness(int? storedPx)
        {
            Settings = new PlatformSettings(
                PlatformSettings.SingletonId, "Acme", "logo.webp", null, null, null, null,
                logoHeight: LogoHeight.FromStored(storedPx));
            Repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings);
            Repository.Setup(r => r.UpdateLogoHeightAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("save"))
                .Returns(Task.CompletedTask);
            Publisher.Setup(p => p.Publish(It.IsAny<PlatformLogoHeightChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((PlatformLogoHeightChangedEvent e, CancellationToken token) =>
                {
                    Published.Add(e);
                    Order.Add(token.CanBeCanceled ? "publish:cancellable" : "publish");
                })
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<PlatformSettingsDto>> Save(int? px)
        {
            var composer = new Mock<IImageUrlComposer>();
            composer.Setup(c => c.Compose(It.IsAny<string?>()))
                .Returns<string?>(key => key is null ? null : $"/uploads/images/{key}");
            return new UpdatePlatformLogoHeightCommandHandler(
                    Repository.Object,
                    new Mock<IUserRepository>().Object,
                    composer.Object,
                    Publisher.Object,
                    TimeProvider.System,
                    NullLogger<UpdatePlatformLogoHeightCommandHandler>.Instance)
                .Handle(new UpdatePlatformLogoHeightCommand(px, Admin), CancellationToken.None);
        }
    }

    [Fact]
    public async Task ThePlatformsHeight_IsSavedOnItsOwn_ThenAnnouncedOnce()
    {
        var harness = new PlatformHarness(storedPx: null);

        var result = await harness.Save(90);

        result.IsError.Should().BeFalse();
        result.Value.LogoHeightPx.Should().Be(90);
        result.Value.LogoUrl.Should().Be("/uploads/images/logo.webp", "the rest of the branding answers as stored");
        harness.Order.Should().Equal("save", "publish");
        var published = harness.Published.Should().ContainSingle().Subject;
        published.BeforePx.Should().BeNull();
        published.AfterPx.Should().Be(90);
        published.TriggeredBy.Should().Be(Admin);
        published.EventId.Should().NotBeEmpty();
        harness.Repository.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Repository.Verify(r => r.UpdateThemeAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ThePlatformsHeight_BackToTheDefault_StoresNone()
    {
        var harness = new PlatformHarness(storedPx: 90);

        var result = await harness.Save(null);

        result.Value.LogoHeightPx.Should().BeNull();
        harness.Settings.LogoHeight.Should().BeNull();
        harness.Published.Should().ContainSingle().Which.BeforePx.Should().Be(90);
    }

    [Fact]
    public async Task ThePlatformsHeight_Unchanged_WritesAndAnnouncesNothing()
    {
        var harness = new PlatformHarness(storedPx: 90);

        var result = await harness.Save(90);

        result.Value.LogoHeightPx.Should().Be(90);
        harness.Order.Should().BeEmpty();
    }

    [Theory]
    [InlineData(23)]
    [InlineData(121)]
    public async Task ThePlatformsHeight_OutOfRange_IsRefusedAndNothingIsWritten(int px)
    {
        var harness = new PlatformHarness(storedPx: 90);

        var result = await harness.Save(px);

        result.FirstError.Code.Should().Be("LogoHeight.OutOfRange");
        harness.Order.Should().BeEmpty();
        harness.Settings.LogoHeight!.Px.Should().Be(90);
    }

    [Fact]
    public void ThePlatformsAdminView_CarriesTheHeight()
    {
        var settings = new PlatformSettings(
            PlatformSettings.SingletonId, "Acme", null, null, null, null, null,
            logoHeight: LogoHeight.FromStored(72));

        PlatformSettingsDto.From(settings, new Mock<IImageUrlComposer>().Object, new Dictionary<Guid, string>())
            .LogoHeightPx.Should().Be(72);
    }

    // --- an application's height ---

    private sealed class ApplicationHarness
    {
        public Mock<IApplicationRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<ApplicationLogoHeightChangedEvent> Published { get; } = [];
        public List<string> Order { get; } = [];
        public ApplicationEntity Application { get; } = TestHelpers.CreateApplication(code: "CRM");

        public ApplicationHarness(int? storedPx, bool stillThere = true)
        {
            Application.LoadLogoHeight(storedPx);
            Repository.Setup(r => r.GetByIdAsync(Application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Repository.Setup(r => r.UpdateLogoHeightAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("save"))
                .ReturnsAsync(stillThere);
            Publisher.Setup(p => p.Publish(It.IsAny<ApplicationLogoHeightChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((ApplicationLogoHeightChangedEvent e, CancellationToken token) =>
                {
                    Published.Add(e);
                    Order.Add(token.CanBeCanceled ? "publish:cancellable" : "publish");
                })
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<ApplicationLogoHeightDto>> Save(int? px, Guid? id = null) =>
            new UpdateApplicationLogoHeightCommandHandler(
                    Repository.Object,
                    Publisher.Object,
                    TimeProvider.System,
                    NullLogger<UpdateApplicationLogoHeightCommandHandler>.Instance)
                .Handle(
                    new UpdateApplicationLogoHeightCommand(id ?? Application.Id, px) { ChangedBy = Admin },
                    CancellationToken.None);
    }

    [Fact]
    public async Task AnApplicationsHeight_IsSavedOnItsOwn_ThenAnnouncedOnce()
    {
        var harness = new ApplicationHarness(storedPx: null);

        var result = await harness.Save(100);

        result.IsError.Should().BeFalse();
        result.Value.LogoHeightPx.Should().Be(100);
        harness.Order.Should().Equal("save", "publish");
        var published = harness.Published.Should().ContainSingle().Subject;
        published.ApplicationId.Should().Be(harness.Application.Id);
        published.ApplicationCode.Should().Be("CRM");
        published.BeforePx.Should().BeNull();
        published.AfterPx.Should().Be(100);
        published.TriggeredBy.Should().Be(Admin);
        harness.Repository.Verify(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnApplicationsHeight_Unchanged_WritesAndAnnouncesNothing()
    {
        var harness = new ApplicationHarness(storedPx: 100);

        var result = await harness.Save(100);

        result.Value.LogoHeightPx.Should().Be(100);
        harness.Order.Should().BeEmpty();
    }

    [Theory]
    [InlineData(23)]
    [InlineData(121)]
    public async Task AnApplicationsHeight_OutOfRange_IsRefusedBeforeAnyRead(int px)
    {
        var harness = new ApplicationHarness(storedPx: null);

        var result = await harness.Save(px);

        result.FirstError.Code.Should().Be("LogoHeight.OutOfRange");
        harness.Order.Should().BeEmpty();
        harness.Repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownApplication_IsNotFound()
    {
        var harness = new ApplicationHarness(storedPx: null);

        var result = await harness.Save(80, id: Guid.NewGuid());

        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        harness.Order.Should().BeEmpty();
    }

    [Fact]
    public async Task AnApplicationDeletedSinceItWasRead_IsNotFound_AndNothingIsAnnounced()
    {
        var harness = new ApplicationHarness(storedPx: null, stillThere: false);

        var result = await harness.Save(80);

        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        harness.Order.Should().Equal("save");
    }

    [Fact]
    public async Task ThePublicBranding_CarriesTheApplicationsHeight()
    {
        var application = TestHelpers.CreateApplication(code: "CRM", name: "Acme CRM");
        application.LoadLogoHeight(96);
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>())).ReturnsAsync(application);

        var result = await new GetPublicBrandingQueryHandler(
                repository.Object, ApplicationTestImages.Composer(), LoginDesignDoubles.NoneActive())
            .Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        result.Value.LogoHeightPx.Should().Be(96);
    }

    // --- the audit rows ---

    private static (Mock<IAuditLogRepository> Repository, Func<AuditLogEntity?> Written) AuditRepository()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        return (repository, () => written);
    }

    [Fact]
    public async Task ThePlatformsAuditRow_RecordsTheHeightBeforeAndAfter_UnderThePlatformSettingsAction()
    {
        var (repository, written) = AuditRepository();
        var notification = new PlatformLogoHeightChangedEvent(
            Guid.NewGuid(), PlatformSettings.SingletonId, null, 90, Admin, DateTimeOffset.UtcNow);

        await new PlatformLogoHeightChangedAuditEventHandler(
                repository.Object, NullLogger<PlatformLogoHeightChangedAuditEventHandler>.Instance)
            .Handle(notification, CancellationToken.None);

        var row = written();
        row.Should().NotBeNull();
        row!.Action.Should().Be(AuditActions.PlatformSettingsUpdated);
        row.ActionType.Should().Be(AuditActionTypes.Administration);
        row.PerformedBy.Should().Be(Admin);
        row.OldValues.Should().Be("""{"logoHeightPx":null}""");
        row.NewValues.Should().Be("""{"logoHeightPx":90}""");
    }

    [Fact]
    public async Task AnApplicationsAuditRow_RecordsTheHeightBeforeAndAfter_UnderItsOwnAction()
    {
        var (repository, written) = AuditRepository();
        var notification = new ApplicationLogoHeightChangedEvent(
            Guid.NewGuid(), Guid.NewGuid(), "CRM", 48, 100, Admin, DateTimeOffset.UtcNow);

        await new ApplicationLogoHeightChangedAuditEventHandler(
                repository.Object, NullLogger<ApplicationLogoHeightChangedAuditEventHandler>.Instance)
            .Handle(notification, CancellationToken.None);

        var row = written();
        row.Should().NotBeNull();
        row!.Action.Should().Be(AuditActions.ApplicationLogoHeightChanged);
        row.ActionType.Should().Be(AuditActionTypes.Application);
        row.EntityId.Should().Be(notification.ApplicationId);
        row.ApplicationId.Should().Be(notification.ApplicationId);
        row.OldValues.Should().Be("""{"logoHeightPx":48}""");
        row.NewValues.Should().Be("""{"logoHeightPx":100}""");
        AuditActions.ByCode[AuditActions.ApplicationLogoHeightChanged].Should().Be(AuditActionTypes.Application);
    }

    [Fact]
    public async Task AFailedAuditWrite_IsLoggedAndNeverReachesTheSave()
    {
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var logger = new Mock<ILogger<ApplicationLogoHeightChangedAuditEventHandler>>();

        var act = () => new ApplicationLogoHeightChangedAuditEventHandler(repository.Object, logger.Object)
            .Handle(
                new ApplicationLogoHeightChangedEvent(Guid.NewGuid(), Guid.NewGuid(), "CRM", null, 80, Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
