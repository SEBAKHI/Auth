using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.SystemSettings;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The console prints a "Default: …" line under every setting, so the value
/// behind it has to be the system's own default and nothing else.
/// <para>
/// <c>BaselineValue</c> looks like it would do — but it coalesces, meaning it
/// reports the FILE value when one exists and only falls back to the default
/// otherwise. That is the right answer to "what would this revert to" and the
/// wrong answer to "what does this system ship with", and the two are
/// indistinguishable on screen.
/// </para>
/// </summary>
public class SystemSettingsDefaultValueProjectionTests
{
    [Fact]
    public void EveryField_CarriesItsRegistryDefaultToTheDto()
    {
        var configuration = new ConfigurationBuilder().Build();
        var snapshot = new StartupValuesSnapshot(
            StartupValuesSnapshot.CaptureValues(configuration),
            StartupValuesSnapshot.CaptureValues(configuration));

        var mismatched = new List<string>();

        foreach (var section in SystemSettingsRegistry.Sections)
        {
            var dto = SystemSettingsProjector.BuildSection(
                section, row: null, configuration, snapshot, modifiedByName: null);

            foreach (var field in section.Fields)
            {
                var projected = dto.Fields.Single(f => f.Path == field.Path);

                // Secret-owned fields carry no values at all, by design.
                var expected = field.Sensitive ? null : field.DefaultValue;

                if (!Equals(Normalize(projected.DefaultValue), Normalize(expected)))
                {
                    mismatched.Add(
                        $"{section.Key}:{field.Path} projected '{projected.DefaultValue}', registry says '{expected}'");
                }
            }
        }

        mismatched.Should().BeEmpty("the console shows this value as the system default");
    }

    [Fact]
    public void DefaultValue_IsNotOverwrittenByAConfiguredFileValue()
    {
        // The distinction that makes this field worth having: with a file value
        // present, BaselineValue becomes that file value while DefaultValue
        // must keep reporting what the system ships with.
        var section = SystemSettingsRegistry.TryGet("GatewayRateLimiting")!;
        var field = SystemSettingsRegistry.TryGetField(section, "AdminPermitLimit")!;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [section.FullKey(field)] = "7"
            })
            .Build();

        var captured = StartupValuesSnapshot.CaptureValues(configuration);
        var dto = SystemSettingsProjector.BuildSection(
            section, row: null, configuration, new StartupValuesSnapshot(captured, captured), null);

        var projected = dto.Fields.Single(f => f.Path == field.Path);

        projected.BaselineValue.Should().Be(7L, "the file layer is what an override would revert to");
        Normalize(projected.DefaultValue).Should().Be(Normalize(field.DefaultValue));
    }

    /// <summary>
    /// Comparable form: the registry holds ints as <c>int</c> while the DTO can
    /// carry them as <c>long</c>, and that difference is not the subject here.
    /// </summary>
    private static string Normalize(object? value) => value switch
    {
        null => "<null>",
        IEnumerable<string> items => string.Join("|", items),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
    };
}
