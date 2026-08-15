using System.Globalization;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Invariants of the settings registry — the single whitelist the write path,
/// the database configuration provider, and the console all trust. Breaking
/// any of these silently widens or corrupts what admins can edit.
/// </summary>
public class SystemSettingsRegistryTests
{
    private static IEnumerable<(SettingSectionDefinition Section, SettingFieldDefinition Field)> AllFields()
        => SystemSettingsRegistry.Sections.SelectMany(s => s.Fields.Select(f => (s, f)));

    [Fact]
    public void SectionKeys_AreUniqueCaseInsensitive()
    {
        SystemSettingsRegistry.Sections
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Should().BeEmpty("section keys are storage primary keys and lookup keys");
    }

    [Fact]
    public void FieldPaths_AreUniqueWithinEachSectionCaseInsensitive()
    {
        foreach (var section in SystemSettingsRegistry.Sections)
        {
            section.Fields
                .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Should().BeEmpty("section '{0}' must not declare duplicate field paths", section.Key);
        }
    }

    [Fact]
    public void FullConfigKeys_AreOwnedByExactlyOneSection()
    {
        // Sections may share a ConfigRoot (grouping by admin concern rather
        // than by appsettings shape), but two sections claiming the SAME
        // config key would let two stored rows fight over one value — the
        // provider would apply whichever it flattened last.
        AllFields()
            .GroupBy(x => x.Section.FullKey(x.Field), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty("each configuration key must belong to exactly one section");
    }

    [Fact]
    public void EditableFields_AreNeverSecretOwned()
    {
        // The guarantee that the database layer and the secret layer hold
        // disjoint key sets: nothing the console can write may ever land on
        // a secret-owned configuration key.
        var leaks = AllFields()
            .Where(x => x.Field.Editable)
            .Select(x => x.Section.FullKey(x.Field))
            .Where(SecretOwnedKeys.IsSecretOwned)
            .ToList();

        leaks.Should().BeEmpty();
    }

    [Fact]
    public void SensitiveFields_AreAlwaysSecretOwned()
    {
        // The counterpart: every field flagged sensitive really is claimed by
        // the secret layer, so it can never be served or stored here.
        var orphans = AllFields()
            .Where(x => x.Field.Sensitive)
            .Select(x => x.Section.FullKey(x.Field))
            .Where(key => !SecretOwnedKeys.IsSecretOwned(key))
            .ToList();

        orphans.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Jwt", "PrivateKeyPath")]
    [InlineData("Jwt", "PrivateKeyPem")]
    [InlineData("Jwt", "PrivateKeyEncrypted")]
    [InlineData("Jwt", "RefreshTokenEncryptedKey")]
    [InlineData("Gateway", "ExpectedToken")]
    public void SecretMaterialFields_ExistAndAreMarkedSensitive(string sectionKey, string fieldPath)
    {
        var section = SystemSettingsRegistry.TryGet(sectionKey);
        section.Should().NotBeNull();

        var field = SystemSettingsRegistry.TryGetField(section!, fieldPath);
        field.Should().NotBeNull();
        field!.Sensitive.Should().BeTrue();
        field.Editable.Should().BeFalse();
    }

    /// <summary>
    /// The Session section is held to the registry's own rule — a field appears
    /// only where a consumer reads it — by naming the permitted set. Adding a
    /// Session:* key that nothing consumes fails here rather than shipping an
    /// operator a control that does nothing, which is exactly what
    /// MaxConcurrentSessions was before it was enforced.
    /// </summary>
    [Fact]
    public void SessionSection_ExposesOnlyTheConsumedFields()
    {
        var session = SystemSettingsRegistry.TryGet("Session");
        session.Should().NotBeNull();

        session!.Fields.Select(f => f.Path).Should().BeEquivalentTo(
        [
            "MaxConcurrentSessions",
            "TerminateOldestOnMax",
            "TerminateSessionsOnPasswordChange",
            "TerminateSessionsOnPasswordReset"
        ]);
        session.Fields.Should().OnlyContain(f => f.Editable);
        // No restart: every one of them is read per sign-in or per password
        // change through IOptionsSnapshot.
        session.Fields.Should().OnlyContain(f => !f.RestartRequired);
    }

    /// <summary>
    /// 0 is the "unlimited" sentinel the enforcement path checks for, so the
    /// lower bound has to admit it. A Min of 1 would make the limit
    /// unremovable once an operator ever set one.
    /// </summary>
    [Fact]
    public void MaxConcurrentSessions_AllowsZeroForUnlimited()
    {
        var session = SystemSettingsRegistry.TryGet("Session");
        var field = SystemSettingsRegistry.TryGetField(session!, "MaxConcurrentSessions");

        field.Should().NotBeNull();
        field!.Kind.Should().Be(SettingKind.Int);
        field.Min.Should().Be(0);
        field.DefaultValue.Should().Be(0);
    }

    [Fact]
    public void EnumFields_AlwaysCarryAllowedValues()
    {
        var enumFields = AllFields().Where(x => x.Field.Kind == SettingKind.Enum).ToList();

        enumFields.Should().NotBeEmpty();
        enumFields.Should().OnlyContain(x =>
            x.Field.AllowedValues != null && x.Field.AllowedValues.Count > 0);
    }

    [Fact]
    public void EditableIntFields_HaveConsistentBounds()
    {
        var editableInts = AllFields()
            .Where(x => x.Field.Kind == SettingKind.Int && x.Field.Editable)
            .ToList();

        editableInts.Should().NotBeEmpty();
        foreach (var (section, field) in editableInts)
        {
            field.Min.Should().NotBeNull("editable int '{0}' needs a lower bound", section.FullKey(field));
            field.Max.Should().NotBeNull("editable int '{0}' needs an upper bound", section.FullKey(field));
            field.Min!.Value.Should().BeLessThanOrEqualTo(field.Max!.Value);
        }
    }

    /// <summary>
    /// A default outside its own range is a self-contradiction the console
    /// renders verbatim: it prints "range 8-128, default 6" and then refuses
    /// the 6 it just called the default. Worse, the number is not decorative —
    /// it is what the field falls back to when neither files nor database
    /// configure the key, so the range would be forbidding a value the system
    /// actually runs on. Password:MinimumLength shipped exactly that.
    /// </summary>
    [Fact]
    public void EditableIntDefaults_FallInsideTheirOwnBounds()
    {
        var offenders = AllFields()
            .Where(x => x.Field.Kind == SettingKind.Int && x.Field.Editable && x.Field.DefaultValue is not null)
            .Select(x => (Key: x.Section.FullKey(x.Field), x.Field,
                Default: Convert.ToInt64(x.Field.DefaultValue, CultureInfo.InvariantCulture)))
            .Where(x => x.Default < x.Field.Min || x.Default > x.Field.Max)
            .Select(x => $"{x.Key} default {x.Default} outside [{x.Field.Min}, {x.Field.Max}]")
            .ToList();

        offenders.Should().BeEmpty("an int field's default must be a value the field itself accepts");
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        SystemSettingsRegistry.TryGet("password").Should().NotBeNull();
        SystemSettingsRegistry.TryGet("PASSWORD").Should().BeSameAs(SystemSettingsRegistry.TryGet("Password"));
        SystemSettingsRegistry.TryGet("NoSuchSection").Should().BeNull();
    }

    [Fact]
    public void TryGetField_IsCaseInsensitiveAndMatchesExactPathOnly()
    {
        var gateway = SystemSettingsRegistry.TryGet("Gateway")!;

        SystemSettingsRegistry.TryGetField(gateway, "exemptpaths").Should().NotBeNull();

        // Array fields match their exact path; indexed element paths are the
        // configuration provider's business, not the registry's.
        SystemSettingsRegistry.TryGetField(gateway, "ExemptPaths:0").Should().BeNull();
    }
}
