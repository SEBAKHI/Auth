using System.Text.Json;
using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Application.Features.Applications.UpdateApplicationAppearance;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationEntity = Auth.Domain.Entities.Application;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// An application's own appearance (phase 5 of the accounts sign-in pages): a switch and a
/// theme with the platform's choices and rules. While the switch is on, the public branding of
/// the sign-in pages carries the theme, whole, in place of the platform's; switching it off
/// keeps the theme saved and the pages show the platform's again.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning its tests red: drop the <c>ThemeEnabled ?</c> from
/// <c>Application.EffectiveTheme</c> (the public branding of a switched-off application);
/// make <c>SetAppearance</c> assign the request's null theme (switching off loses it); remove
/// the publish from the handler; remove the catch from the audit handler.
/// </remarks>
public class ApplicationAppearanceTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static ThemeChoices Choices(string theme = "blue", string radius = "large") => new(
        new ThemeColorChoice("neutral"),
        new ThemeColorChoice(theme),
        new ThemeColorChoice("cyan"),
        radius,
        "subtle",
        new ThemeSizeChoice("comfortable"),
        new ThemeSizeChoice("comfortable"));

    private static PlatformTheme Theme(string theme = "blue", string radius = "large") =>
        Choices(theme, radius).ToTheme().Value;

    // --- the entity ---

    [Fact]
    public void SwitchingOn_WithATheme_StoresBoth_AndTheThemeTakesEffect()
    {
        var application = TestHelpers.CreateApplication();

        var changed = application.SetAppearance(true, Theme(), Admin);

        changed.Value.Should().BeTrue();
        application.ThemeEnabled.Should().BeTrue();
        application.Theme.Should().Be(Theme());
        application.EffectiveTheme.Should().Be(Theme());
        application.ModifiedBy.Should().Be(Admin);
    }

    [Fact]
    public void SwitchingOff_WithoutATheme_KeepsTheSavedOne_AndItStopsTakingEffect()
    {
        var application = TestHelpers.CreateApplication();
        application.SetAppearance(true, Theme(), Admin);

        var changed = application.SetAppearance(false, null, Admin);

        changed.Value.Should().BeTrue();
        application.ThemeEnabled.Should().BeFalse();
        application.Theme.Should().Be(Theme(), "switching off keeps the saved appearance");
        application.EffectiveTheme.Should().BeNull("the platform's appearance shows while it is off");
    }

    [Fact]
    public void SwitchingOn_WithNothingSentOrSaved_IsRefused_AndChangesNothing()
    {
        var application = TestHelpers.CreateApplication();

        var changed = application.SetAppearance(true, null, Admin);

        changed.FirstError.Should().Be(ApplicationErrors.AppearanceThemeRequired);
        application.ThemeEnabled.Should().BeFalse();
        application.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public void SwitchingOn_WithoutATheme_UsesTheSavedOne()
    {
        var application = TestHelpers.CreateApplication();
        application.LoadAppearance(Theme(), enabled: false);

        application.SetAppearance(true, null, Admin).Value.Should().BeTrue();

        application.EffectiveTheme.Should().Be(Theme());
    }

    [Fact]
    public void SavingWhatIsStored_ChangesNothing()
    {
        var application = TestHelpers.CreateApplication();
        application.LoadAppearance(Theme(), enabled: true);

        application.SetAppearance(true, Theme(), Admin).Value.Should().BeFalse();
        application.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public void AStoredSwitch_WithNoReadableTheme_ReadsAsOff()
    {
        var application = TestHelpers.CreateApplication();

        application.LoadAppearance(null, enabled: true);

        application.ThemeEnabled.Should().BeFalse();
        application.EffectiveTheme.Should().BeNull();
    }

    // --- the validator ---

    [Fact]
    public void Validator_RefusesSwitchingOnWithoutATheme_AtTheThemeMember()
    {
        var result = new UpdateApplicationAppearanceCommandValidator()
            .Validate(new UpdateApplicationAppearanceCommand(Guid.NewGuid(), true, null));

        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorCode.Should().Be("Application.AppearanceThemeRequired");
        result.Errors[0].PropertyName.Should().Be(nameof(UpdateApplicationAppearanceCommand.Theme));
    }

    [Fact]
    public void Validator_AcceptsSwitchingOffWithoutATheme()
    {
        new UpdateApplicationAppearanceCommandValidator()
            .Validate(new UpdateApplicationAppearanceCommand(Guid.NewGuid(), false, null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RefusesAnEmptyApplicationId()
    {
        new UpdateApplicationAppearanceCommandValidator()
            .Validate(new UpdateApplicationAppearanceCommand(Guid.Empty, false, null))
            .Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(ApplicationErrors.IdRequired.Code);
    }

    // --- the handler ---

    private sealed class Harness
    {
        public Mock<IApplicationRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<ApplicationAppearanceChangedEvent> Published { get; } = [];
        public List<string> Order { get; } = [];
        public ApplicationEntity Application { get; } = TestHelpers.CreateApplication(code: "CRM");

        public Harness()
        {
            Repository.Setup(r => r.GetByIdAsync(Application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Repository.Setup(r => r.UpdateAppearanceAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("save"))
                .ReturnsAsync(true);
            Publisher.Setup(p => p.Publish(It.IsAny<ApplicationAppearanceChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((ApplicationAppearanceChangedEvent e, CancellationToken token) =>
                {
                    Published.Add(e);
                    Order.Add(token.CanBeCanceled ? "publish:cancellable" : "publish");
                })
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<Auth.Application.DTOs.ApplicationAppearanceDto>> Save(bool enabled, ThemeChoices? theme, Guid? id = null) =>
            new UpdateApplicationAppearanceCommandHandler(
                    Repository.Object,
                    Publisher.Object,
                    TimeProvider.System,
                    NullLogger<UpdateApplicationAppearanceCommandHandler>.Instance)
                .Handle(
                    new UpdateApplicationAppearanceCommand(id ?? Application.Id, enabled, theme) { ChangedBy = Admin },
                    CancellationToken.None);
    }

    [Fact]
    public async Task Handler_SwitchingOn_SavesThenPublishesOnce_WithBeforeAndAfter()
    {
        var harness = new Harness();

        var result = await harness.Save(true, Choices());

        result.IsError.Should().BeFalse();
        result.Value.Enabled.Should().BeTrue();
        result.Value.Theme!.Theme.Preset.Should().Be("blue");
        harness.Order.Should().Equal("save", "publish");
        var published = harness.Published.Should().ContainSingle().Subject;
        published.ApplicationId.Should().Be(harness.Application.Id);
        published.ApplicationCode.Should().Be("CRM");
        published.Before.Should().Be(new ApplicationAppearanceSnapshot(false, null));
        published.After.Should().Be(ApplicationAppearanceSnapshot.Of(true, Theme()));
        published.TriggeredBy.Should().Be(Admin);
        published.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handler_HoldsTheThemeToThePlatformsRules()
    {
        var harness = new Harness();

        // A monochrome theme of another base, which the platform's appearance refuses too.
        var result = await harness.Save(true, Choices(theme: "stone"));

        result.FirstError.Code.Should().Be("SystemSettings.ThemeColorUnavailableForBase");
        harness.Repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_SwitchingOff_KeepsTheTheme_InTheStoreAndTheAnswer()
    {
        var harness = new Harness();
        harness.Application.LoadAppearance(Theme(), enabled: true);

        var result = await harness.Save(false, null);

        result.Value.Enabled.Should().BeFalse();
        result.Value.Theme!.Radius.Should().Be("large");
        harness.Repository.Verify(r => r.UpdateAppearanceAsync(
            It.Is<ApplicationEntity>(a => !a.ThemeEnabled && a.Theme == Theme()), It.IsAny<CancellationToken>()), Times.Once);
        harness.Published.Should().ContainSingle().Which.After.Should().Be(ApplicationAppearanceSnapshot.Of(false, Theme()));
    }

    [Fact]
    public async Task Handler_SavingWhatIsStored_WritesNothing_AndPublishesNothing()
    {
        var harness = new Harness();
        harness.Application.LoadAppearance(Theme(), enabled: true);

        var result = await harness.Save(true, Choices());

        result.Value.Enabled.Should().BeTrue();
        harness.Repository.Verify(r => r.UpdateAppearanceAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_UnknownApplication_IsNotFound()
    {
        var harness = new Harness();

        var result = await harness.Save(true, Choices(), id: Guid.NewGuid());

        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handler_ApplicationDeletedBeforeTheWrite_IsNotFound_AndAnnouncesNothing()
    {
        var harness = new Harness();
        harness.Repository.Setup(r => r.UpdateAppearanceAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await harness.Save(true, Choices());

        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        harness.Published.Should().BeEmpty();
    }

    // --- the audit row ---

    private static ApplicationAppearanceChangedEvent Changed() => new(
        Guid.NewGuid(), Guid.NewGuid(), "CRM",
        new ApplicationAppearanceSnapshot(false, null),
        ApplicationAppearanceSnapshot.Of(true, Theme()),
        Admin,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task TheAuditRow_RecordsTheSwitchAndTheThemeBeforeAndAfter()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        var notification = Changed();

        await new ApplicationAppearanceChangedAuditEventHandler(
                repository.Object, NullLogger<ApplicationAppearanceChangedAuditEventHandler>.Instance)
            .Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.ApplicationAppearanceChanged);
        written.ActionType.Should().Be(AuditActionTypes.Application);
        written.PerformedBy.Should().Be(Admin);
        written.EntityType.Should().Be("Application");
        written.EntityId.Should().Be(notification.ApplicationId);
        written.ApplicationId.Should().Be(notification.ApplicationId);
        using var before = JsonDocument.Parse(written.OldValues!);
        before.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
        before.RootElement.GetProperty("theme").ValueKind.Should().Be(JsonValueKind.Null);
        using var after = JsonDocument.Parse(written.NewValues!);
        after.RootElement.GetProperty("enabled").GetBoolean().Should().BeTrue();
        after.RootElement.GetProperty("theme").GetProperty("theme").GetProperty("preset").GetString().Should().Be("blue");
        after.RootElement.GetProperty("theme").GetProperty("controlSize").GetProperty("preset").GetString()
            .Should().Be("comfortable");
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("applicationCode").GetString().Should().Be("CRM");
    }

    [Fact]
    public async Task TheAuditHandler_SwallowsAndLogsAFailure()
    {
        // The save has committed: a throwing handler would turn it into an error answer.
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var logger = new Mock<ILogger<ApplicationAppearanceChangedAuditEventHandler>>();

        var act = () => new ApplicationAppearanceChangedAuditEventHandler(repository.Object, logger.Object)
            .Handle(Changed(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task TheAuditHandler_LetsItsOwnCancellationThrough()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var act = () => new ApplicationAppearanceChangedAuditEventHandler(
                repository.Object, NullLogger<ApplicationAppearanceChangedAuditEventHandler>.Instance)
            .Handle(Changed(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- the public branding of the sign-in pages ---

    private static async Task<PublicBrandingDto> Branding(ApplicationEntity application)
    {
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.GetByCodeAsync(application.Code, It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var result = await new GetPublicBrandingQueryHandler(repository.Object, ApplicationTestImages.Composer(), LoginDesignDoubles.NoneActive())
            .Handle(new GetPublicBrandingQuery(application.Code), CancellationToken.None);
        return result.Value;
    }

    [Fact]
    public async Task PublicBranding_SwitchedOn_CarriesTheWholeTheme()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        application.LoadAppearance(Theme(), enabled: true);

        var branding = await Branding(application);

        branding.Theme.Should().NotBeNull();
        branding.Theme!.Base.Preset.Should().Be("neutral");
        branding.Theme.Theme.Preset.Should().Be("blue");
        branding.Theme.Chart.Preset.Should().Be("cyan");
        branding.Theme.Radius.Should().Be("large");
        branding.Theme.MenuAccent.Should().Be("subtle");
        branding.Theme.ControlSize.Preset.Should().Be("comfortable");
        branding.Theme.TableRowHeight.Preset.Should().Be("comfortable");
    }

    [Fact]
    public async Task PublicBranding_SwitchedOff_CarriesNoTheme_ThoughOneIsSaved()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        application.LoadAppearance(Theme(), enabled: false);

        (await Branding(application)).Theme.Should().BeNull();
    }

    [Fact]
    public async Task PublicBranding_NoneSaved_CarriesNoTheme()
    {
        (await Branding(TestHelpers.CreateApplication(code: "CRM"))).Theme.Should().BeNull();
    }
}
