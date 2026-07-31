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
        ["Gateway"] = new GatewaySettings()
    };

    [Fact]
    public void RegistryDefaults_MatchSettingsClassDefaults()
    {
        foreach (var section in SystemSettingsRegistry.Sections)
        {
            if (!SettingsInstances.TryGetValue(section.Key, out var instance))
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
