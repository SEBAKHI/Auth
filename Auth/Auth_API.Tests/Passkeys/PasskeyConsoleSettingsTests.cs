using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Features.SystemSettings.ResetSystemSettings;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Administration;
using Auth_API.Tests.Infrastructure;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR C (IN 8, T-C9): the <c>Passkeys</c> section in the console. <c>Enabled</c> is hot and
/// off by default; <c>RpId</c> needs a restart and is empty by default. A change of the RP ID is
/// refused when a browser would refuse it (PK2: not a host name, or one that does not cover a
/// first-party origin) and while live passkeys are bound to another RP ID (PK3, at the save and
/// at the reset). Switching passkeys off is never refused for the RP ID. Both refusals are the
/// existing <c>SystemSettings.InvalidFieldValue</c>: no new published code.
/// </summary>
/// <remarks>
/// Deliberate breaks, each seen red: drop the Passkeys case from the section rules; judge the RP
/// ID when it does not change; drop the PK3 call from the reset handler, or from the save
/// handler; read the RP ID at start-up before the database layer joins.
/// </remarks>
public class PasskeyConsoleSettingsTests
{
    private const string Console = "https://console.example.com";
    private const string Accounts = "https://accounts.example.com";

    private static SettingSectionDefinition Section()
    {
        var section = SystemSettingsRegistry.TryGet(PasskeySettings.SectionName);
        section.Should().NotBeNull("the console lists the Passkeys section from S20 PR C on");
        return section!;
    }

    [Fact]
    public void TheSection_IsAnEditableSecuritySection_OfTwoFields()
    {
        var section = Section();

        section.Group.Should().Be(SettingGroups.Security);
        section.Editable.Should().BeTrue();
        section.Fields.Select(field => field.Path).Should().Equal("Enabled", "RpId");

        var enabled = section.Fields.Single(field => field.Path == "Enabled");
        enabled.Kind.Should().Be(SettingKind.Bool);
        enabled.RestartRequired.Should().BeFalse("PasskeyFeature reads it per request");
        enabled.DefaultValue.Should().Be(false);

        var rpId = section.Fields.Single(field => field.Path == "RpId");
        rpId.Kind.Should().Be(SettingKind.String);
        rpId.RestartRequired.Should().BeTrue("PasskeyRpIdAtStartup reads it once, at start-up");
        rpId.DefaultValue.Should().Be("");
        rpId.Sensitive.Should().BeFalse();
        rpId.ReadOnly.Should().BeFalse();
    }

    // --- PK2: the section rule ---------------------------------------------------------------

    private static Dictionary<string, string?> Running(string rpId, params string[] origins)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Passkeys:RpId"] = rpId,
            ["Passkeys:Enabled"] = "true",
        };
        for (var index = 0; index < origins.Length; index++)
        {
            values[$"IdentityProvider:FirstPartySpaOrigins:{index}"] = origins[index];
        }

        return values;
    }

    private static List<Error> Save(string payloadJson, Dictionary<string, string?> now, Dictionary<string, string?>? baseline = null)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        var values = payload.RootElement.EnumerateObject()
            .Select(property => new KeyValuePair<string, JsonElement>(property.Name, property.Value.Clone()))
            .ToList();
        var errors = new List<Error>();
        SystemSettingsValueValidator.ValidateSectionRules(
            Section(),
            values,
            errors,
            key => now.TryGetValue(key, out var value) ? value : null,
            requestOrigin: null,
            baselineValue: key => (baseline ?? now).TryGetValue(key, out var value) ? value : null);
        return errors;
    }

    private static void ShouldRefuseRpId(List<Error> errors, string naming)
    {
        errors.Should().NotBeEmpty();
        errors.Should().OnlyContain(error => error.Code == "SystemSettings.InvalidFieldValue");
        var args = (object[])errors[0].Metadata!["args"];
        args[0].Should().Be("RpId");
        string.Join(" | ", errors.Select(error => (string)((object[])error.Metadata!["args"])[1]))
            .Should().Contain(naming);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("Example.COM")]
    [InlineData(" example.com ")]
    public void ANewRpId_ThatCoversEveryFirstPartyOrigin_IsAccepted(string rpId) =>
        Save($$"""{"RpId":"{{rpId}}"}""", Running("", Console, Accounts)).Should().BeEmpty();

    [Fact]
    public void Localhost_CoversTheDevelopmentOriginsOnEveryPort() =>
        Save("""{"RpId":"localhost"}""", Running("", "https://localhost:5173", "https://localhost:5174"))
            .Should().BeEmpty();

    [Fact]
    public void ANewRpId_ThatMissesAFirstPartyOrigin_IsRefused_NamingIt() =>
        ShouldRefuseRpId(
            Save("""{"RpId":"example.com"}""", Running("", Console, "https://accounts.example.net")),
            "does not cover the first-party origin 'https://accounts.example.net'");

    [Fact]
    public void ANewRpId_OfOneAppOnly_IsRefused_ForTheOtherApp() =>
        ShouldRefuseRpId(
            Save("""{"RpId":"console.example.com"}""", Running("", Console, Accounts)),
            $"does not cover the first-party origin '{Accounts}'");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("https://example.com")]
    [InlineData("example.com:443")]
    [InlineData("example.com/path")]
    [InlineData("com")]
    [InlineData("exa mple.com")]
    public void ANewRpId_ThatIsNotAHostName_IsRefused(string rpId) =>
        ShouldRefuseRpId(
            Save(JsonSerializer.Serialize(new Dictionary<string, string> { ["RpId"] = rpId }), Running("", Console, Accounts)),
            "is not a host name");

    [Fact]
    public void AnEmptyRpId_SwitchesPasskeysOff_AndPassesTheRule() =>
        Save("""{"RpId":""}""", Running("example.com", Console, Accounts)).Should().BeEmpty(
            "an empty RP ID is no RP ID a browser refuses; whether live passkeys forbid it is PK3's");

    [Fact]
    public void SwitchingPasskeysOff_IsNeverRefusedForTheRpId()
    {
        // The origins moved since the RP ID was set (T-C9 lets that save through, and the posture
        // switches passkeys off). Turning the switch off must still save: it is the rollback.
        var now = Running("example.com", Console, "https://accounts.example.net");

        Save("""{"Enabled":false,"RpId":"example.com"}""", now).Should().BeEmpty();
        Save("""{"Enabled":false}""", now).Should().BeEmpty("an omitted RP ID falls back to the same value");
    }

    [Fact]
    public void ASaveThatOmitsTheRpId_IsJudgedOnTheBaselineItFallsBackTo()
    {
        // The override row held the RP ID; the save of Enabled alone replaces the row, so the RP
        // ID returns to the file's, which misses an origin: that is a change, and it is judged.
        var now = Running("example.com", Console, "https://accounts.example.net");
        var baseline = Running("console.example.com", Console, "https://accounts.example.net");

        ShouldRefuseRpId(Save("""{"Enabled":true}""", now, baseline), "does not cover the first-party origin");
    }

    // --- PK3: the handlers --------------------------------------------------------------------

    private static Mock<IUserPasskeyRepository> Rows(bool liveOfAnotherRpId)
    {
        var rows = new Mock<IUserPasskeyRepository>();
        rows.Setup(r => r.HasLiveOfAnotherRpIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveOfAnotherRpId);
        return rows;
    }

    private static IConfiguration Configuration(string rpId) => SystemSettingsTestSupport.BuildConfiguration(
        ("Passkeys:RpId", rpId),
        ("Passkeys:Enabled", "true"),
        ("IdentityProvider:FirstPartySpaOrigins:0", Console),
        ("IdentityProvider:FirstPartySpaOrigins:1", Accounts));

    private static (UpdateSystemSettingsCommandHandler Handler, Mock<ISystemSettingsRepository> Settings) UpdateHandler(
        IConfiguration running, IUserPasskeyRepository passkeys)
    {
        var settings = new Mock<ISystemSettingsRepository>();
        settings.Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));
        var handler = new UpdateSystemSettingsCommandHandler(
            settings.Object, new Mock<IUserRepository>().Object, running, SystemSettingsTestSupport.SnapshotOf(running),
            new Mock<ISystemSettingsReloader>().Object, new Mock<IPublisher>().Object,
            new Mock<IIdpSessionIdleLimitChange>().Object, new PasskeyRpIdChange(passkeys),
            NullLogger<UpdateSystemSettingsCommandHandler>.Instance);
        return (handler, settings);
    }

    private static UpdateSystemSettingsCommand SaveCommand(string json) =>
        new(PasskeySettings.SectionName, SystemSettingsTestSupport.Json(json), null, Guid.NewGuid());

    [Theory]
    [InlineData("""{"RpId":"example.net"}""", "example.net")]
    [InlineData("""{"RpId":""}""", "")]
    public async Task UpdateHandler_ANewRpIdWhileLivePasskeysAreBoundToAnother_IsRefused_AndNothingIsSaved(
        string json, string asked)
    {
        var rows = Rows(liveOfAnotherRpId: true);
        var running = SystemSettingsTestSupport.BuildConfiguration(
            ("Passkeys:RpId", "example.com"),
            ("IdentityProvider:FirstPartySpaOrigins:0", "https://console.example.net"),
            ("IdentityProvider:FirstPartySpaOrigins:1", "https://accounts.example.net"));
        var (handler, settings) = UpdateHandler(running, rows.Object);

        var result = await handler.Handle(SaveCommand(json), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        result.FirstError.Description.Should().Contain("--retire-passkey-rp-id",
            "the sentence names the setup tool's retirement ([S20-O10])");
        rows.Verify(r => r.HasLiveOfAnotherRpIdAsync(asked, It.IsAny<CancellationToken>()), Times.Once());
        settings.Verify(
            r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task UpdateHandler_ANewRpIdWithNoLivePasskeyOfAnother_IsSaved()
    {
        var rows = Rows(liveOfAnotherRpId: false);
        var (handler, settings) = UpdateHandler(Configuration(""), rows.Object);

        var result = await handler.Handle(SaveCommand("""{"RpId":"example.com"}"""), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Description : "");
        settings.Verify(
            r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task UpdateHandler_ASaveThatLeavesTheRpId_NeverAsksTheRows()
    {
        var rows = Rows(liveOfAnotherRpId: true);
        var (handler, _) = UpdateHandler(Configuration("example.com"), rows.Object);

        var result = await handler.Handle(
            SaveCommand("""{"Enabled":false,"RpId":"Example.com"}"""), CancellationToken.None);

        result.IsError.Should().BeFalse("switching passkeys off is the rollback, and it never moves the RP ID");
        rows.Verify(r => r.HasLiveOfAnotherRpIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task UpdateHandler_AnotherSection_NeverAsksTheRows()
    {
        var rows = Rows(liveOfAnotherRpId: true);
        var running = SystemSettingsTestSupport.BuildConfiguration(("TwoFactor:EnforceForPlatformAdmins", "false"));
        var (handler, _) = UpdateHandler(running, rows.Object);

        var result = await handler.Handle(
            new UpdateSystemSettingsCommand("TwoFactor", SystemSettingsTestSupport.Json("""{"EnforceForPlatformAdmins":true}"""), null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        rows.Verify(r => r.HasLiveOfAnotherRpIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static (ResetSystemSettingsCommandHandler Handler, Mock<ISystemSettingsRepository> Settings) ResetHandler(
        IConfiguration file, IConfiguration running, IUserPasskeyRepository passkeys)
    {
        var settings = new Mock<ISystemSettingsRepository>();
        settings.Setup(r => r.GetAsync(PasskeySettings.SectionName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow(PasskeySettings.SectionName, """{"RpId":"example.com"}"""));
        var handler = new ResetSystemSettingsCommandHandler(
            settings.Object, running, SystemSettingsTestSupport.SnapshotOf(file), new Mock<ISystemSettingsReloader>().Object,
            new Mock<IPublisher>().Object, new Mock<IIdpSessionIdleLimitChange>().Object,
            new PasskeyRpIdChange(passkeys), NullLogger<ResetSystemSettingsCommandHandler>.Instance);
        return (handler, settings);
    }

    [Fact]
    public async Task ResetHandler_BringingBackTheFilesEmptyRpIdWhilePasskeysAreLive_IsRefused_AndNothingIsDeleted()
    {
        // The console set the RP ID (the override row); the file has none. Resetting would empty
        // it under live passkeys: as a save of "" would, it is refused.
        var rows = Rows(liveOfAnotherRpId: true);
        var (handler, settings) = ResetHandler(Configuration(""), Configuration("example.com"), rows.Object);

        var result = await handler.Handle(
            new ResetSystemSettingsCommand(PasskeySettings.SectionName, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        result.FirstError.Description.Should().Contain("--retire-passkey-rp-id");
        rows.Verify(r => r.HasLiveOfAnotherRpIdAsync("", It.IsAny<CancellationToken>()), Times.Once());
        settings.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task ResetHandler_WithNoLivePasskeyOfAnother_Resets()
    {
        var rows = Rows(liveOfAnotherRpId: false);
        var (handler, settings) = ResetHandler(Configuration(""), Configuration("example.com"), rows.Object);

        var result = await handler.Handle(
            new ResetSystemSettingsCommand(PasskeySettings.SectionName, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Description : "");
        settings.Verify(r => r.DeleteAsync(PasskeySettings.SectionName, It.IsAny<CancellationToken>()), Times.Once());
    }

    // --- The RP ID the console saves reaches the passkeys ------------------------------------

    [Fact]
    public void Program_ReadsTheRpIdAtStartUp_OnlyAfterTheDatabaseLayerJoined()
    {
        var program = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));

        var dbLayer = program.IndexOf("((IConfigurationBuilder)builder.Configuration).Add(dbSettingsSource);", StringComparison.Ordinal);
        var reads = Regex.Matches(program, @"PasskeyRpIdAtStartup\.From\(builder\.Configuration\)");

        dbLayer.Should().BePositive("the database settings layer joins the configuration in Program.cs");
        reads.Should().ContainSingle("the RP ID is read once, at start-up");
        reads[0].Index.Should().BeGreaterThan(dbLayer,
            "read before the database layer joins, an RP ID saved in the console would never reach the passkeys");
    }
}
