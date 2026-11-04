using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Security.Keyring;
using Auth.Application.SystemSettings;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV1 and RV3: one horizon, computed from the settings' bounds, and every writer of the
/// revocation list known. A writer that bypasses <c>ICredentialRevocationService</c>, a literal
/// where a bound belongs, or a window computed from the lifetime in force would each let a
/// revocation lapse before a token it covers.
/// </summary>
/// <remarks>
/// Deliberate breaks, each seen red: 1440 as a literal in the registry; the S12 window as
/// 1440 min + 300 s instead of the horizon; a direct <c>BlacklistSession</c> call in
/// <c>TerminateSessionCommandHandler</c>; the one-hour retention back in the blacklist.
/// </remarks>
public class RevocationHorizonGuardTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine([ApiSourceScan.SolutionDirectory(), .. path]));

    /// <summary>The production files that call <paramref name="method"/> on an instance.</summary>
    private static List<string> CallersOf(string method)
    {
        var call = new Regex($@"\.{method}\(");
        var callers = ApiSourceScan.ProductionSources()
            .Where(source => call.IsMatch(source.Source))
            .Select(source => Path.GetFileName(source.File))
            .Order(StringComparer.Ordinal)
            .ToList();
        callers.Should().NotBeEmpty($"the scan must find the callers of {method}");
        return callers;
    }

    [Fact]
    public void TheHorizon_IsComputedFromTheBounds_NeverALiteral()
    {
        var source = Source("Auth.Application", "Configuration", "RevocationHorizon.cs");

        source.Should().Contain("TimeSpan.FromMinutes(JwtSettings.MaxAccessTokenLifetimeMinutes) + JwtSettings.MaxClockSkew");
        source.Should().NotContain("1445");
        RevocationHorizon.User.Should().Be(TimeSpan.FromMinutes(1445));
    }

    [Fact]
    public void TheRegistry_TakesTheLifetimeAndSkewBoundsFromJwtSettings()
    {
        var registry = Regex.Replace(Source("Auth.Application", "SystemSettings", "SystemSettingsRegistry.cs"), @"\s+", " ");

        registry.Should().Contain(
            "\"AccessTokenLifetimeMinutes\", SettingKind.Int, Min: Configuration.JwtSettings.MinAccessTokenLifetimeMinutes, Max: Configuration.JwtSettings.MaxAccessTokenLifetimeMinutes,");
        registry.Should().Contain(
            "\"ClockSkewSeconds\", SettingKind.Int, RestartRequired: true, Min: Configuration.JwtSettings.MinClockSkewSeconds, Max: Configuration.JwtSettings.MaxClockSkewSeconds,");

        var jwt = SystemSettingsRegistry.Sections.Single(section => section.Key == "Jwt").Fields;
        var lifetime = jwt.Single(field => field.Path == "AccessTokenLifetimeMinutes");
        lifetime.Min.Should().Be(1, "the values the console has always offered");
        lifetime.Max.Should().Be(1440);
        var skew = jwt.Single(field => field.Path == "ClockSkewSeconds");
        skew.Min.Should().Be(0);
        skew.Max.Should().Be(300);
    }

    [Fact]
    public void TheSigningKeyWindow_IsTheRevocationHorizon()
    {
        var source = Source("Auth.Application", "Security", "Keyring", "SigningKeyOverlap.cs");

        source.Should().Contain("promotedAtUtc + RevocationHorizon.User;");
        source.Should().NotContain("FromMinutes").And.NotContain("FromSeconds");
        var promotedAt = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        SigningKeyOverlap.PreviousVerifyUntil(promotedAt).Should().Be(promotedAt.AddMinutes(1440).AddSeconds(300));
    }

    [Fact]
    public void TheBlacklist_HasNoOneHourRetentionLeft()
    {
        var source = Source("Auth.Infrastructure", "Authentication", "TokenBlacklistService.cs");

        source.Should().NotContain("UserRevocationRetention").And.NotContain("FromHours(1)");
        source.Should().Contain("revokedAt + RevocationHorizon.User");
        source.Should().Contain("UtcNow + RevocationHorizon.User");
        source.Should().Contain("expiresAt + JwtSettings.MaxClockSkew");
    }

    [Fact]
    public void SessionEntries_AreWrittenOnlyByTheCredentialRevocationService()
    {
        CallersOf("BlacklistSession").Should().Equal("CredentialRevocationService.cs");
    }

    [Fact]
    public void UserEntries_AreWrittenOnlyByTheRevocationServiceAndTheSignOutEverywhere()
    {
        CallersOf("BlacklistAllUserTokens").Should().Equal("CredentialRevocationService.cs", "LogoutCommandHandler.cs");
    }

    [Fact]
    public void TokenEntries_AreWrittenOnlyByTheSignOutAndTheRevocationEndpoint()
    {
        CallersOf("BlacklistToken").Should().Equal("LogoutCommandHandler.cs", "RevokeTokenCommandHandler.cs");
    }

    /// <summary>
    /// The writers that revoke everything (ARR-0390): each inherits the horizon through the
    /// service, and a new one is a reviewed change to this list.
    /// </summary>
    [Fact]
    public void EveryRevokeEverythingCaller_IsKnown()
    {
        CallersOf("RevokeAllCredentialsAsync").Should().Equal(
            "AccountDeletionRequestor.cs",
            "AccountDeletionWorker.cs",
            "DeactivateAccountCommandHandler.cs",
            "DeleteUserCommandHandler.cs",
            "ExecuteAccountDeletionCommandHandler.cs",
            "HardDeleteUserCommandHandler.cs",
            "LockAccountCommandHandler.cs",
            "RefreshTokenCommandHandler.cs",
            "ResetPasswordCommandHandler.cs",
            "ResetUserTwoFactorCommandHandler.cs");
        CallersOf("RevokeCredentialsAsync").Should().Equal(
            "ChangePasswordCommandHandler.cs",
            "ConfirmAuthenticatorReplacementCommandHandler.cs",
            "DisableTwoFactorCommandHandler.cs",
            "RemovePasswordCommandHandler.cs",
            "SecondFactorChangeRevocation.cs",
            "SignInWithEmailCodeCommandHandler.cs",
            "TerminateAllSessionsCommandHandler.cs");
    }
}
