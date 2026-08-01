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
        ["Gateway"] = new GatewaySettings(),
        ["Email"] = new EmailSettings(),
        ["Notifications"] = new NotificationSettings(),
        ["AccountDeletion"] = new AccountDeletionSettings(),
        ["ImageStorage"] = new ImageStorageSettings(),
        ["IdentityProvider"] = new IdentityProviderSettings(),
        // ExternalAuth is omitted: its Google/Apple sub-objects default to
        // null (provider treats that as "not configured"), so nested class
        // defaults cannot be resolved by reflection.
        ["SecretManagement"] = new SecretManagementSettings()
    };

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
