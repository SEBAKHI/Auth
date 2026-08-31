using Auth.Shared.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Guards <see cref="AuthDataProtectionExtensions.ParseStorageMode"/> against the
/// failure it used to have: an unrecognised value resolved to PlainText in silence.
///
/// <para>
/// Silence was the whole problem. Because the configured mode and the effective mode
/// then agreed, the "falling back" warning in Program.cs never fired, and with the
/// shipped AutoGenerateKeys default the JWT signing key and the refresh-token HMAC
/// key were generated and written as PLAIN TEXT into an appsettings file inside the
/// deploy tree. One typo in one string was the whole distance between encryption at
/// rest and no encryption at rest, and nothing in the logs said so.
/// </para>
///
/// <para>
/// Blank stays PlainText on purpose: it means "not configured", it matches the class
/// default on SecretManagementSettings, and the API Gateway reads the key straight
/// out of configuration where it may legitimately be absent. Throwing there would
/// stop deployments that are behaving exactly as documented.
/// </para>
/// </summary>
public class SecretStorageModeParsingTests
{
    [Theory]
    [InlineData("PlainText", SecretStorageMode.PlainText)]
    [InlineData("plaintext", SecretStorageMode.PlainText)]
    [InlineData("Certificate", SecretStorageMode.Certificate)]
    [InlineData("CERTIFICATE", SecretStorageMode.Certificate)]
    [InlineData("Dpapi", SecretStorageMode.Dpapi)]
    [InlineData("dPaPi", SecretStorageMode.Dpapi)]
    // Surrounding whitespace is tolerated deliberately: a stray space in a JSON value
    // is an editing artefact, not an operator changing the security posture.
    [InlineData(" Certificate ", SecretStorageMode.Certificate)]
    [InlineData("DPAPI ", SecretStorageMode.Dpapi)]
    public void KnownValues_ParseCaseInsensitively(string configured, SecretStorageMode expected)
    {
        AuthDataProtectionExtensions.ParseStorageMode(configured).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankValue_MeansNotConfigured_AndStaysPlainText(string? configured)
    {
        // The gateway reads this key directly from configuration, where it can be
        // absent by design. Throwing here would break a documented deployment shape.
        AuthDataProtectionExtensions.ParseStorageMode(configured).Should().Be(SecretStorageMode.PlainText);
    }

    [Theory]
    [InlineData("Certficate")]   // the transposition that motivated this guard
    [InlineData("Certificat")]
    [InlineData("plain-text")]
    [InlineData("None")]
    public void UnrecognisedValue_Throws_RatherThanSilentlyPickingTheWeakestMode(string configured)
    {
        var act = () => AuthDataProtectionExtensions.ParseStorageMode(configured);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a known storage mode*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    public void NumericValue_Throws(string configured)
    {
        // Enum.TryParse accepts the underlying ordinals, so a stray "0" would have
        // resolved to whichever member sits first — PlainText — reaching the same
        // silent downgrade through a different door. The contract is a name.
        var act = () => AuthDataProtectionExtensions.ParseStorageMode(configured);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ThrownMessage_ListsTheValidValues()
    {
        // The operator reading this line at 3am must not have to open the source.
        var act = () => AuthDataProtectionExtensions.ParseStorageMode("Certficate");

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        foreach (var name in Enum.GetNames<SecretStorageMode>())
        {
            message.Should().Contain(name);
        }
    }
}
