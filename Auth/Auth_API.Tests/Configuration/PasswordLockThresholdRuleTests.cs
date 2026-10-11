using System.Text.Json;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.SystemSettings;
using ErrorOr;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// X12, review 64 F7: the console refuses a save that leaves
/// <c>Password:MaxFailedAttempts</c> (Max′) at or above
/// <c>Password:ConsecutiveFailureLimit</c> (Hard′). The failure statement tests the
/// hard limit first, so at Max′ = Hard′ the first lock would be the hard one and the
/// per-address ceiling would silently become Hard′. Judged on the values as they will
/// be after the save (an omitted field falls back to the baseline), through the
/// existing <c>SystemSettings.InvalidFieldValue</c> code; a reset is never refused
/// for a file value, which the normalizer clamps at run time.
/// </summary>
/// <remarks>Deliberate break: remove the Password case from <c>ValidateSectionRules</c>.</remarks>
public class PasswordLockThresholdRuleTests
{
    private static SettingSectionDefinition Section()
    {
        var section = SystemSettingsRegistry.TryGet("Password");
        section.Should().NotBeNull();
        return section!;
    }

    private static Dictionary<string, string?> Layer(int threshold, int limit) => new(StringComparer.OrdinalIgnoreCase)
    {
        [Section().FullKey("MaxFailedAttempts")] = threshold.ToString(),
        [Section().FullKey("ConsecutiveFailureLimit")] = limit.ToString(),
    };

    private static List<Error> Validate(
        (string Path, int Value)[] payload, Dictionary<string, string?> baseline, Dictionary<string, string?>? now = null)
    {
        var values = payload
            .Select(entry => new KeyValuePair<string, JsonElement>(
                entry.Path, JsonDocument.Parse(entry.Value.ToString()).RootElement.Clone()))
            .ToList();
        var errors = new List<Error>();

        SystemSettingsValueValidator.ValidateSectionRules(
            Section(),
            values,
            errors,
            key => (now ?? baseline).TryGetValue(key, out var value) ? value : null,
            requestOrigin: null,
            baselineValue: key => baseline.TryGetValue(key, out var value) ? value : null);

        return errors;
    }

    public static TheoryData<string, int, int, int> Refused => new()
    {
        // The field the save carries, its value, and the other field's baseline.
        { "MaxFailedAttempts", 10, 5, 10 },
        { "MaxFailedAttempts", 20, 5, 10 },
        { "ConsecutiveFailureLimit", 10, 10, 100 },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Save_LeavingTheThresholdAtOrAboveTheLimit_IsRefused(string field, int value, int baselineThreshold, int baselineLimit)
    {
        var errors = Validate([(field, value)], Layer(baselineThreshold, baselineLimit));

        errors.Should().ContainSingle();
        errors[0].Code.Should().Be("SystemSettings.InvalidFieldValue");
        ((object[])errors[0].Metadata!["args"])[0].Should().Be("MaxFailedAttempts");
    }

    [Fact]
    public void Refused_IsNotEmpty() => Refused.Should().NotBeEmpty();

    [Theory]
    [InlineData(9, 10)]
    [InlineData(5, 100)]
    public void Save_KeepingATimedLockBeforeTheHardOne_IsAccepted(int threshold, int limit)
    {
        Validate([("MaxFailedAttempts", threshold), ("ConsecutiveFailureLimit", limit)], Layer(5, 100))
            .Should().BeEmpty();
    }

    [Fact]
    public void Save_IsJudgedOnTheValuesAfterTheSave()
    {
        // A database override holds the limit at 100 today; the save replaces the
        // section's override row without it, so the limit falls back to the file's 10.
        var errors = Validate([("MaxFailedAttempts", 50)], baseline: Layer(5, 10), now: Layer(5, 100));

        errors.Should().ContainSingle(e => e.Code == "SystemSettings.InvalidFieldValue");
    }

    [Fact]
    public void Reset_IsNeverRefusedForAFileValue()
    {
        Validate([], Layer(10, 10)).Should().BeEmpty();
    }
}
