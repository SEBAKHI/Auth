using System.Text.Json;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Features.SystemSettings.ResetSystemSettings;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Administration;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// OI-155 PR C, owner decision D-C-1 (a) (TC-11): the console refuses a save or a reset
/// that leaves <c>IdentityProvider:IdpBrowserSessionIdleMinutes</c> above 0 and below twice
/// <c>Jwt:AccessTokenLifetimeMinutes</c>. An application counts a use of its SSO session
/// only when it refreshes, about once per access-token lifetime, and its sign-in ends with
/// that session (D-155-9), so a shorter idle limit signs people out while they work. Judged
/// on the values after the write, from either section, through the existing
/// <c>SystemSettings.InvalidFieldValue</c> code; the message names the other setting and
/// its value.
/// </summary>
/// <remarks>
/// Deliberate breaks: remove the call from the IdentityProvider case, from the Jwt case,
/// the <c>idle == 0</c> exemption, or the reset handler's <c>liveValue</c>; each turns a
/// test here red.
/// </remarks>
public class IdleLimitAccessTokenPairRuleTests
{
    private const string IdleKey = "IdentityProvider:IdpBrowserSessionIdleMinutes";
    private const string AccessKey = "Jwt:AccessTokenLifetimeMinutes";

    private static SettingSectionDefinition Section(string key)
    {
        var section = SystemSettingsRegistry.TryGet(key);
        section.Should().NotBeNull();
        return section!;
    }

    /// <summary>
    /// A save of <paramref name="sectionKey"/> carrying one field, over a running
    /// configuration (<paramref name="now"/>) whose database layer sits on
    /// <paramref name="baseline"/>.
    /// </summary>
    private static List<Error> Save(
        string sectionKey, string field, int value,
        Dictionary<string, string?> now, Dictionary<string, string?>? baseline = null)
    {
        var errors = new List<Error>();
        SystemSettingsValueValidator.ValidateSectionRules(
            Section(sectionKey),
            [new KeyValuePair<string, JsonElement>(field, JsonDocument.Parse(value.ToString()).RootElement.Clone())],
            errors,
            key => now.TryGetValue(key, out var v) ? v : null,
            requestOrigin: null,
            baselineValue: key => (baseline ?? now).TryGetValue(key, out var v) ? v : null);
        return errors.Where(e => e.Code == "SystemSettings.InvalidFieldValue" && IsPairError(e)).ToList();
    }

    private static bool IsPairError(Error error) =>
        ((object[])error.Metadata!["args"])[0] is "IdpBrowserSessionIdleMinutes" or "AccessTokenLifetimeMinutes";

    private static Dictionary<string, string?> Running(int idle, int access) => new(StringComparer.OrdinalIgnoreCase)
    {
        [IdleKey] = idle.ToString(),
        [AccessKey] = access.ToString(),
        ["IdentityProvider:AccountsBaseUrl"] = "https://accounts.example.com",
        ["Jwt:Issuer"] = "https://auth.example.com",
        ["Jwt:Audience"] = "https://auth.example.com",
    };

    private static void ShouldBeRefused(List<Error> errors, string field, string naming)
    {
        errors.Should().ContainSingle();
        var args = (object[])errors[0].Metadata!["args"];
        args[0].Should().Be(field);
        ((string)args[1]).Should().Contain(naming, "the message names the other setting and its current value");
    }

    [Fact]
    public void IdentityProviderSave_IdleTwentyWithAFifteenMinuteAccessToken_IsRefused() =>
        ShouldBeRefused(
            Save("IdentityProvider", "IdpBrowserSessionIdleMinutes", 20, Running(idle: 30, access: 15)),
            "IdpBrowserSessionIdleMinutes", $"{AccessKey} (15)");

    [Fact]
    public void JwtSave_AccessTwentyWithAThirtyMinuteIdleLimit_IsRefused() =>
        ShouldBeRefused(
            Save("Jwt", "AccessTokenLifetimeMinutes", 20, Running(idle: 30, access: 15)),
            "AccessTokenLifetimeMinutes", $"{IdleKey} (30)");

    [Theory]
    [InlineData(0, 15)]
    [InlineData(0, 1440)]
    [InlineData(30, 15)]
    [InlineData(1440, 720)]
    public void IdentityProviderSave_KeepingThePair_IsAccepted(int idle, int access) =>
        Save("IdentityProvider", "IdpBrowserSessionIdleMinutes", idle, Running(idle: 30, access: access))
            .Should().BeEmpty();

    [Theory]
    [InlineData(15, 30)]
    [InlineData(1440, 0)]
    [InlineData(1, 2)]
    public void JwtSave_KeepingThePair_IsAccepted(int access, int idle) =>
        Save("Jwt", "AccessTokenLifetimeMinutes", access, Running(idle: idle, access: 15))
            .Should().BeEmpty();

    [Fact]
    public void ASaveThatOmitsTheField_IsJudgedOnTheBaselineItFallsBackTo()
    {
        // The save replaces the section's whole override row: the idle limit returns to
        // the baseline's 20, which breaks the pair with the running 15-minute token.
        var errors = new List<Error>();
        var now = Running(idle: 60, access: 15);
        var baseline = Running(idle: 20, access: 15);
        SystemSettingsValueValidator.ValidateSectionRules(
            Section("IdentityProvider"),
            [new KeyValuePair<string, JsonElement>("IdpSessionLifetimeDays", JsonDocument.Parse("7").RootElement.Clone())],
            errors,
            key => now.TryGetValue(key, out var v) ? v : null,
            requestOrigin: null,
            baselineValue: key => baseline.TryGetValue(key, out var v) ? v : null);

        ShouldBeRefused(errors.Where(IsPairError).ToList(), "IdpBrowserSessionIdleMinutes", $"{AccessKey} (15)");
    }

    [Fact]
    public void KeysAbsentFromEveryLayer_RunWithTheClassDefaults_WhichKeepThePair() =>
        Save("Jwt", "Issuer", 0, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
            .Should().BeEmpty("30 minutes idle and a 15-minute access token by default");

    // --- through the handlers -------------------------------------------------

    private static UpdateSystemSettingsCommandHandler UpdateHandler(IConfiguration running)
    {
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));
        return new UpdateSystemSettingsCommandHandler(
            rows.Object, new Mock<IUserRepository>().Object, running, SystemSettingsTestSupport.SnapshotOf(running),
            new Mock<ISystemSettingsReloader>().Object, new Mock<IPublisher>().Object,
            new Mock<IIdpSessionIdleLimitChange>().Object, NullLogger<UpdateSystemSettingsCommandHandler>.Instance);
    }

    [Fact]
    public async Task UpdateHandler_SavingAJwtLifetimeThatBreaksThePair_IsRefused()
    {
        var running = SystemSettingsTestSupport.BuildConfiguration(
            ("Jwt:Issuer", "https://auth.example.com"), ("Jwt:Audience", "https://auth.example.com"),
            (IdleKey, "30"));

        var result = await UpdateHandler(running).Handle(
            new UpdateSystemSettingsCommand("Jwt", SystemSettingsTestSupport.Json("""{"AccessTokenLifetimeMinutes":20}"""), null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        result.FirstError.Description.Should().Contain($"{IdleKey} (30)");
    }

    [Fact]
    public async Task ResetHandler_ToAFileIdleLimitThatBreaksThePairWithTheRunningToken_IsRefused()
    {
        // The file layer has a 20-minute idle limit and a 5-minute token; the database
        // raised the token to 15. Resetting IdentityProvider brings back 20 against the
        // RUNNING 15, which the reset does not change: refused. Read from the file layer
        // instead (the reset's own baseline), the token would be 5 and the reset accepted.
        var file = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"), (IdleKey, "20"), (AccessKey, "5"));
        var running = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"), (IdleKey, "40"), (AccessKey, "15"));
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.GetAsync("IdentityProvider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("IdentityProvider", """{"IdpBrowserSessionIdleMinutes":40}"""));

        var result = await new ResetSystemSettingsCommandHandler(
                rows.Object, running, SystemSettingsTestSupport.SnapshotOf(file), new Mock<ISystemSettingsReloader>().Object,
                new Mock<IPublisher>().Object, new Mock<IIdpSessionIdleLimitChange>().Object,
                NullLogger<ResetSystemSettingsCommandHandler>.Instance)
            .Handle(new ResetSystemSettingsCommand("IdentityProvider", Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        result.FirstError.Description.Should().Contain($"{AccessKey} (15)");
        rows.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
