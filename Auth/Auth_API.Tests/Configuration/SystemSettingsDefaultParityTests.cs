using System.Collections;
using System.Globalization;
using Auth.Application.Configuration;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The registry's DefaultValue entries duplicate the settings-class defaults
/// (IConfiguration cannot see class defaults, so the console needs them to
/// display what actually runs). This guard fails when the two drift apart.
/// </summary>
public class SystemSettingsDefaultParityTests
{
    private static readonly Dictionary<string, object> SettingsInstances = new()
    {
        ["Jwt"] = new JwtSettings(),
        ["Password"] = new PasswordSettings(),
        ["Session"] = new SessionSettings(),
        ["TwoFactor"] = new TwoFactorSettings(),
        // Array properties deliberately start empty (a non-empty initializer is an
        // unremovable prefix once the configuration binder appends to it), so the
        // EFFECTIVE default is what the production PostConfigure produces. Applying
        // it here keeps this guard pointed at the value a consumer really receives —
        // and at the value the console displays as the fallback.
        ["Gateway"] = Normalized(new GatewaySettings()),
        ["Email"] = new EmailSettings(),
        ["Notifications"] = new NotificationSettings(),
        ["AccountDeletion"] = new AccountDeletionSettings(),
        // The ExpiredDataCleanup section binds to this root. It went unguarded
        // until the retention window for pending registrations was added, so
        // its ten older defaults are checked here for the first time too.
        ["DataRetention"] = new DataRetentionSettings(),
        ["ImageStorage"] = Normalized(new ImageStorageSettings()),
        ["Registration"] = new RegistrationSettings(),
        ["IdentityProvider"] = Normalized(new IdentityProviderSettings()),
        // ExternalAuth is omitted: its Google/Apple sub-objects default to
        // null (provider treats that as "not configured"), so nested class
        // defaults cannot be resolved by reflection.
        ["SecretManagement"] = new SecretManagementSettings()
    };

    private static GatewaySettings Normalized(GatewaySettings settings)
    {
        SettingsArrayNormalizer.Apply(settings);
        return settings;
    }

    private static ImageStorageSettings Normalized(ImageStorageSettings settings)
    {
        SettingsArrayNormalizer.Apply(settings);
        return settings;
    }

    private static IdentityProviderSettings Normalized(IdentityProviderSettings settings)
    {
        SettingsArrayNormalizer.Apply(settings);
        return settings;
    }

    /// <summary>
    /// The parity walk above skips a field the registry does not list, so it cannot
    /// notice that one of the refresh-cookie settings went missing. These three are
    /// named: each must be registered with the default its class carries.
    /// </summary>
    [Theory]
    [InlineData("IdentityProvider", "SpaRefreshCookieEnabled", "False")]
    [InlineData("IdentityProvider", "FirstPartySpaOrigins", "")]
    [InlineData("Jwt", "RefreshReplayGraceSeconds", "30")]
    public void RefreshCookieSettings_AreRegisteredWithTheirClassDefaults(
        string sectionKey, string fieldPath, string expected)
    {
        var section = SystemSettingsRegistry.Sections.Single(s => s.Key == sectionKey);
        var field = section.Fields.SingleOrDefault(f => f.Path == fieldPath);

        field.Should().NotBeNull($"{sectionKey}:{fieldPath} must be editable from the console");
        Normalize(field!.DefaultValue).Should().Be(expected);
        Normalize(ResolveProperty(SettingsInstances[section.ConfigRoot], fieldPath)).Should().Be(expected);
    }

    /// <summary>
    /// The window a sign-in stays recent enough to change two-factor
    /// authentication. Named rather than left to the walk below, which skips a
    /// field the registry does not list: it must be registered, hot, bounded to
    /// 5–60 and default to 15 in both places.
    /// </summary>
    [Fact]
    public void ReauthenticationWindow_IsRegisteredWithItsClassDefaultAndRange()
    {
        var section = SystemSettingsRegistry.Sections.Single(s => s.Key == "TwoFactor");
        var field = section.Fields.SingleOrDefault(f => f.Path == "ReauthenticationMaxAgeMinutes");

        field.Should().NotBeNull("TwoFactor:ReauthenticationMaxAgeMinutes must be editable from the console");
        Normalize(field!.DefaultValue).Should().Be("15");
        Normalize(ResolveProperty(SettingsInstances["TwoFactor"], "ReauthenticationMaxAgeMinutes")).Should().Be("15");
        field.Min.Should().Be(TwoFactorSettings.MinReauthenticationMaxAgeMinutes).And.Be(5);
        field.Max.Should().Be(TwoFactorSettings.MaxReauthenticationMaxAgeMinutes).And.Be(60);
        field.RestartRequired.Should().BeFalse("the guard reads it per request");
    }

    /// <summary>
    /// The switch in front of the emailed code a first second factor needs (X02 PR
    /// B). It ships ON in both places, so the owner's decision applies from the
    /// first deploy, and it is hot: an operator who must let binds through during a
    /// mail outage flips it without a restart.
    /// </summary>
    [Fact]
    public void FirstFactorEmailProofSwitch_IsRegistered_OnByDefault_AndHot()
    {
        var section = SystemSettingsRegistry.Sections.Single(s => s.Key == "TwoFactor");
        var field = section.Fields.SingleOrDefault(f => f.Path == "RequireEmailCodeForFirstFactor");

        field.Should().NotBeNull("TwoFactor:RequireEmailCodeForFirstFactor must be editable from the console");
        field!.Kind.Should().Be(SettingKind.Bool);
        Normalize(field.DefaultValue).Should().Be("True");
        Normalize(ResolveProperty(SettingsInstances["TwoFactor"], "RequireEmailCodeForFirstFactor")).Should().Be("True");
        field.RestartRequired.Should().BeFalse("the policy reads it per request");
    }

    /// <summary>
    /// The parity walk below skips, in silence, every section whose ConfigRoot has
    /// no entry in <see cref="SettingsInstances"/> — so the one protection a
    /// security default has against drifting from its class is a dictionary line
    /// that nothing obliges anyone to write. These roots carry security switches
    /// whose shipped default must be the one that runs; each must be walked.
    /// </summary>
    [Fact]
    public void RequiredConfigRoots_AreCovered()
    {
        string[] required = ["TwoFactor"];

        foreach (var root in required)
        {
            SystemSettingsRegistry.Sections.Should().Contain(
                section => section.ConfigRoot == root,
                $"{root} must be a section the console can show");
            SettingsInstances.Should().ContainKey(root,
                $"without an instance of its settings class, the parity walk skips {root} and its defaults are unguarded");
        }
    }

    [Fact]
    public void RegistryDefaults_MatchSettingsClassDefaults()
    {
        foreach (var section in SystemSettingsRegistry.Sections)
        {
            // Keyed by ConfigRoot, not Key: several console sections can
            // present one appsettings section (e.g. the AccountDeletion root).
            if (!SettingsInstances.TryGetValue(section.ConfigRoot, out var instance))
            {
                continue;
            }

            foreach (var field in section.Fields)
            {
                if (field.DefaultValue is null || field.Sensitive)
                {
                    continue;
                }

                var classDefault = ResolveProperty(instance, field.Path);

                Normalize(classDefault).Should().Be(
                    Normalize(field.DefaultValue),
                    $"registry default for {section.Key}:{field.Path} must mirror the settings-class default");
            }
        }
    }

    private static object? ResolveProperty(object instance, string path)
    {
        object? current = instance;
        foreach (var segment in path.Split(':'))
        {
            current = current?.GetType().GetProperty(segment)?.GetValue(current);
        }

        return current;
    }

    private static string Normalize(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        IEnumerable enumerable => string.Join("|", enumerable.Cast<object>()),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>"
    };
}
