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

    [Fact]
    public void SessionSection_ExposesOnlyTheTwoTerminationToggles()
    {
        var session = SystemSettingsRegistry.TryGet("Session");
        session.Should().NotBeNull();

        session!.Fields.Select(f => f.Path).Should().BeEquivalentTo(
        [
            "TerminateSessionsOnPasswordChange",
            "TerminateSessionsOnPasswordReset"
        ]);
        session.Fields.Should().OnlyContain(f => f.Kind == SettingKind.Bool && f.Editable);
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
