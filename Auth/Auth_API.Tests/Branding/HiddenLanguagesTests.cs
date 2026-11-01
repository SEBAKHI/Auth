using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPlatformBranding;
using Auth.Application.Features.Platform.UpdatePlatformHiddenLanguages;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Authorization;
using Auth_API.Modules.Administration.Controllers;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.Branding;

/// <summary>
/// The languages an administrator hides from the language menus of both apps (UI batch P5, item
/// 11, owner decision D8): supported codes only, never English, so at least one language always
/// stays visible. Stored in its own column, saved on its own endpoint, published on the anonymous
/// branding so every page knows what not to list or download.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning tests here red: accept <c>en</c> in <c>HiddenLanguages.Create</c>
/// (the English cases); drop the <c>Equals</c> short-cut in <c>SetHiddenLanguages</c> (an unchanged
/// save writes and publishes); leave <c>HiddenLanguages</c> out of <c>PlatformSettingsDto.From</c>
/// or the branding handler (the answers lose it); let <c>FromStored</c> keep English (the stored
/// cases); write the column from another statement (the statement count).
/// </remarks>
public class HiddenLanguagesTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    // --- the value ---

    [Fact]
    public void NoLanguages_HidesNone()
    {
        HiddenLanguages.Create(null).Value.Should().Be(HiddenLanguages.None);
        HiddenLanguages.Create([]).Value.Codes.Should().BeEmpty();
    }

    [Fact]
    public void TheCodes_AreKeptOnce_LowerCased_InTheMenusOrder()
    {
        var hidden = HiddenLanguages.Create(["FA", "fr", " tr ", "fr", "ar"]);

        hidden.IsError.Should().BeFalse();
        hidden.Value.Codes.Should().Equal("ar", "tr", "fr", "fa");
    }

    [Fact]
    public void TwoSavesOfTheSameChoice_AreEqual_HoweverWritten()
    {
        HiddenLanguages.Create(["zh", "ar"]).Value
            .Should().Be(HiddenLanguages.Create(["ar", "ZH", "ar"]).Value);
        HiddenLanguages.Create(["ar"]).Value.Should().NotBe(HiddenLanguages.Create(["fa"]).Value);
    }

    [Fact]
    public void EveryLanguageButEnglish_CanBeHidden_SoOneAlwaysStaysVisible()
    {
        var allButEnglish = Languages.Supported.Where(code => code != Languages.Default).ToArray();

        HiddenLanguages.Create(allButEnglish).Value.Codes.Should().Equal(allButEnglish);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData(" en ")]
    public void English_CannotBeHidden(string code)
    {
        var hidden = HiddenLanguages.Create(["fr", code]);

        hidden.IsError.Should().BeTrue();
        hidden.FirstError.Code.Should().Be("HiddenLanguages.DefaultLanguageRequired");
        hidden.FirstError.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("ar-SA")]
    [InlineData("")]
    [InlineData(null)]
    public void ACodeTheAppsDoNotShip_IsRefused(string? code)
    {
        var hidden = HiddenLanguages.Create(["fr", code]);

        hidden.IsError.Should().BeTrue();
        hidden.FirstError.Code.Should().Be("HiddenLanguages.UnsupportedLanguage");
        hidden.FirstError.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void AStoredList_DropsWhatTheRulesRefuse_InsteadOfFailingTheRead()
    {
        HiddenLanguages.FromStored(["en", "xx", "FR", null, "ar"]).Codes.Should().Equal("ar", "fr");
        HiddenLanguages.FromStored(null).Should().Be(HiddenLanguages.None);
    }

    // --- the stored form ---

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("not json", new string[0])]
    [InlineData("{\"fr\":true}", new string[0])]
    [InlineData("[\"en\",\"fr\",\"xx\",\"ar\"]", new[] { "ar", "fr" })]
    public void TheColumn_ReadsBack_NeverFailing(string? stored, string[] read)
    {
        PlatformSettingsRepository.HiddenLanguagesJson.Read(stored).Codes.Should().Equal(read);
    }

    [Fact]
    public void NoneHidden_IsStoredAsNull_AndARemovalReadsBackAsNone()
    {
        PlatformSettingsRepository.HiddenLanguagesJson.Write(HiddenLanguages.None).Should().BeNull();

        var hidden = HiddenLanguages.Create(["fa", "zh"]).Value;
        var stored = PlatformSettingsRepository.HiddenLanguagesJson.Write(hidden);
        stored.Should().Be("""["zh","fa"]""");
        PlatformSettingsRepository.HiddenLanguagesJson.Read(stored).Should().Be(hidden);

        // Remove every hidden language, then read the row again: nothing stays behind (an array
        // setting bound by configuration would keep a prefix; a column cannot).
        var removed = PlatformSettingsRepository.HiddenLanguagesJson.Write(HiddenLanguages.Create([]).Value);
        PlatformSettingsRepository.HiddenLanguagesJson.Read(removed).Codes.Should().BeEmpty();
    }

    private static string TableSource() =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                "PlatformSettings.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    [Fact]
    public void TheColumn_IsAppendedLast_NullableWithoutADefault_AndLongEnoughForEveryLanguage()
    {
        var column = OrganizationCreationSchemaTests.DeclaredColumns(TableSource(), "PlatformSettings")[^1];

        column.Name.Should().Be("HiddenLanguages", "appended, the publish is an ALTER TABLE ADD, not a rebuild");
        var match = Regex.Match(column.Definition, @"^NVARCHAR\((\d+)\)\s+NULL$");
        match.Success.Should().BeTrue();
        var allButEnglish = HiddenLanguages.Create(Languages.Supported.Where(code => code != Languages.Default)).Value;
        PlatformSettingsRepository.HiddenLanguagesJson.Write(allButEnglish)!.Length
            .Should().BeLessThanOrEqualTo(int.Parse(match.Groups[1].Value));
    }

    [Fact]
    public void TheColumn_HoldsJsonOnly_UnderANamedConstraint()
    {
        TableSource().Should().Contain(
            "CONSTRAINT [CK_PlatformSettings_HiddenLanguagesIsJson] CHECK ([HiddenLanguages] IS NULL OR ISJSON([HiddenLanguages]) = 1)");
    }

    [Fact]
    public void OnlyTheDedicatedStatement_WritesTheColumn()
    {
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "PlatformSettingsRepository.cs"));

        // The dedicated method, up to the next member's documentation.
        var start = source.IndexOf("public async Task UpdateHiddenLanguagesAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = source.IndexOf("/// <", start, StringComparison.Ordinal);
        var dedicated = source[start..end];
        var rest = source[..start] + source[end..];

        Regex.Matches(dedicated, @"\[HiddenLanguages\] = @HiddenLanguages").Should().ContainSingle();
        Regex.Matches(dedicated, @"@HiddenLanguages\b").Should().HaveCount(2, "the update and the insert of the seed row");
        Regex.Matches(rest, @"@HiddenLanguages\b").Should().BeEmpty(
            "a branding save built from a stale copy must never put back older hidden languages, by any statement");
        Regex.Matches(rest, @"\[HiddenLanguages\]").Should().ContainSingle("the column is only read elsewhere");
    }

    // --- saving ---

    private sealed class Harness
    {
        public Mock<IPlatformSettingsRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<PlatformHiddenLanguagesChangedEvent> Published { get; } = [];
        public List<string> Order { get; } = [];
        public PlatformSettings Settings { get; }

        public Harness(params string[] stored)
        {
            Settings = new PlatformSettings(
                PlatformSettings.SingletonId, "Acme", "logo.webp", null, null, null, null,
                hiddenLanguages: HiddenLanguages.FromStored(stored));
            Repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings);
            Repository.Setup(r => r.UpdateHiddenLanguagesAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("save"))
                .Returns(Task.CompletedTask);
            Publisher.Setup(p => p.Publish(It.IsAny<PlatformHiddenLanguagesChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((PlatformHiddenLanguagesChangedEvent e, CancellationToken token) =>
                {
                    Published.Add(e);
                    Order.Add(token.CanBeCanceled ? "publish:cancellable" : "publish");
                })
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<PlatformSettingsDto>> Save(params string?[]? codes)
        {
            var composer = new Mock<IImageUrlComposer>();
            composer.Setup(c => c.Compose(It.IsAny<string?>()))
                .Returns<string?>(key => key is null ? null : $"/uploads/images/{key}");
            return new UpdatePlatformHiddenLanguagesCommandHandler(
                    Repository.Object,
                    new Mock<IUserRepository>().Object,
                    composer.Object,
                    Publisher.Object,
                    TimeProvider.System,
                    NullLogger<UpdatePlatformHiddenLanguagesCommandHandler>.Instance)
                .Handle(new UpdatePlatformHiddenLanguagesCommand(codes, Admin), CancellationToken.None);
        }
    }

    [Fact]
    public async Task TheHiddenLanguages_AreSavedOnTheirOwn_ThenAnnouncedOnce()
    {
        var harness = new Harness();

        var result = await harness.Save("fr", "ar");

        result.IsError.Should().BeFalse();
        result.Value.HiddenLanguages.Should().Equal("ar", "fr");
        result.Value.LogoUrl.Should().Be("/uploads/images/logo.webp", "the rest of the settings answers as stored");
        harness.Order.Should().Equal("save", "publish");
        var published = harness.Published.Should().ContainSingle().Subject;
        published.Before.Should().BeEmpty();
        published.After.Should().Equal("ar", "fr");
        published.TriggeredBy.Should().Be(Admin);
        published.EventId.Should().NotBeEmpty();
        harness.Repository.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Repository.Verify(r => r.UpdateThemeAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Repository.Verify(r => r.UpdateFontsAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShowingEveryLanguageAgain_StoresNone()
    {
        var harness = new Harness("fr");

        var result = await harness.Save();

        result.Value.HiddenLanguages.Should().BeEmpty();
        harness.Settings.HiddenLanguages.Should().Be(HiddenLanguages.None);
        harness.Published.Should().ContainSingle().Which.Before.Should().Equal("fr");
    }

    [Fact]
    public async Task TheSameLanguages_WriteAndAnnounceNothing()
    {
        var harness = new Harness("fr", "zh");

        var result = await harness.Save("ZH", "fr");

        result.Value.HiddenLanguages.Should().Equal("fr", "zh");
        harness.Order.Should().BeEmpty();
    }

    [Theory]
    [InlineData("en", "HiddenLanguages.DefaultLanguageRequired")]
    [InlineData("de", "HiddenLanguages.UnsupportedLanguage")]
    public async Task ARefusedList_WritesNothing_AndKeepsWhatWasHidden(string code, string expected)
    {
        var harness = new Harness("fr");

        var result = await harness.Save("ar", code);

        result.FirstError.Code.Should().Be(expected);
        harness.Order.Should().BeEmpty();
        harness.Settings.HiddenLanguages.Codes.Should().Equal("fr");
    }

    [Fact]
    public void TheAdminView_CarriesTheHiddenLanguages()
    {
        var settings = new PlatformSettings(
            PlatformSettings.SingletonId, "Acme", null, null, null, null, null,
            hiddenLanguages: HiddenLanguages.FromStored(["ur"]));

        PlatformSettingsDto.From(settings, new Mock<IImageUrlComposer>().Object, new Dictionary<Guid, string>())
            .HiddenLanguages.Should().Equal("ur");
    }

    [Fact]
    public async Task ThePublicBranding_CarriesTheHiddenLanguages_AndNoneByDefault()
    {
        static async Task<PlatformBrandingDto> Branding(PlatformSettings? stored)
        {
            var repository = new Mock<IPlatformSettingsRepository>();
            repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);
            var result = await new GetPlatformBrandingQueryHandler(
                    repository.Object, new Mock<IImageUrlComposer>().Object,
                    TestHelpers.CreateOptions(new IdentityProviderSettings()),
                    TestHelpers.CreateOptions(new RegistrationSettings()),
                    TestHelpers.CreateOptions(new EmailSettings()),
                    LoginDesignDoubles.NoneActive())
                .Handle(new GetPlatformBrandingQuery(), CancellationToken.None);
            return result.Value;
        }

        (await Branding(null)).HiddenLanguages.Should().BeEmpty("an installation nobody configured shows every language");
        (await Branding(new PlatformSettings(
                PlatformSettings.SingletonId, "Acme", null, null, null, null, null,
                hiddenLanguages: HiddenLanguages.FromStored(["fa", "tr"]))))
            .HiddenLanguages.Should().Equal("tr", "fa");
    }

    [Fact]
    public void TheEndpoint_IsForAdministratorsWhoManageThePlatformSettings()
    {
        var method = typeof(PlatformSettingsController).GetMethod(nameof(PlatformSettingsController.UpdateHiddenLanguages))!;

        method.GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("hidden-languages");
        method.GetCustomAttribute<RequirePermissionAttribute>()!.Permission
            .Should().Be(PermissionCodes.PlatformSettings.Manage);
    }

    // --- the audit row ---

    [Fact]
    public async Task TheAuditRow_RecordsTheLanguagesBeforeAndAfter_UnderThePlatformSettingsAction()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);

        await new PlatformHiddenLanguagesChangedAuditEventHandler(
                repository.Object, NullLogger<PlatformHiddenLanguagesChangedAuditEventHandler>.Instance)
            .Handle(
                new PlatformHiddenLanguagesChangedEvent(
                    Guid.NewGuid(), PlatformSettings.SingletonId, [], ["fr", "zh"], Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.PlatformSettingsUpdated);
        written.ActionType.Should().Be(AuditActionTypes.Administration);
        written.PerformedBy.Should().Be(Admin);
        written.OldValues.Should().Be("""{"hiddenLanguages":[]}""");
        written.NewValues.Should().Be("""{"hiddenLanguages":["fr","zh"]}""");
    }

    [Fact]
    public async Task AFailedAuditWrite_IsLoggedAndNeverReachesTheSave()
    {
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => new PlatformHiddenLanguagesChangedAuditEventHandler(
                repository.Object, NullLogger<PlatformHiddenLanguagesChangedAuditEventHandler>.Instance)
            .Handle(
                new PlatformHiddenLanguagesChangedEvent(
                    Guid.NewGuid(), PlatformSettings.SingletonId, ["fr"], [], Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
