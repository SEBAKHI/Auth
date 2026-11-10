using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S20 PR C (IN 9, T-C12): the privacy policy a new installation starts with says what a passkey
/// leaves with the service, in every language: one bullet under "Data we collect", right after
/// the credentials, naming the public key, the identifier, the counter, the name, the two dates
/// and the sync flag, and saying that the fingerprint, the face, the PIN and the private key never
/// leave the device. Each language's <c>ContentJson</c> must still parse: a broken one would
/// leave that language without a policy on a fresh database.
/// </summary>
/// <remarks>
/// The seed reaches new installations only (each row is inserted while it is absent, and the
/// version published while nothing is); an existing deployment publishes its own revision in the
/// console ([S20-O4]). Deliberate break: remove the bullet from one language: red.
/// </remarks>
public class PrivacyPolicyPasskeySeedContractTests
{
    private const string Seed = "16_PrivacyPolicyContent.sql";

    [Fact]
    public void EveryLanguage_DescribesWhatAPasskeyStores_RightAfterTheCredentials()
    {
        var documents = Documents();

        documents.Keys.Should().BeEquivalentTo(Languages.Supported, "one policy document per supported language");
        foreach (var (language, document) in documents)
        {
            var bullets = document.GetProperty("sections")[0].GetProperty("bullets")
                .EnumerateArray().Select(bullet => bullet.GetString()!).ToList();

            bullets.Count(bullet => bullet.Contains("passkey", StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, $"the {language} policy has one passkey bullet");
            bullets[2].Should().ContainEquivalentOf("passkey", $"in {language} it follows the credentials bullet");
            bullets[2].Should().NotContain("{{", "it interpolates nothing");
        }
    }

    [Fact]
    public void TheEnglishBullet_NamesEveryStoredValue_AndWhatNeverLeavesTheDevice()
    {
        var english = Documents()["en"].GetProperty("sections")[0].GetProperty("bullets")[2].GetString()!;

        english.Should().ContainAll(
            "public key", "credential identifier", "signature counter", "name you give it",
            "when you added it", "last used it", "syncs it",
            "fingerprint", "face", "PIN", "private key", "never leave your device");
    }

    [Fact]
    public void TheHeader_NoLongerNamesTheRemovedBundle()
    {
        var seed = File.ReadAllText(SeedPath());

        seed.Should().NotContain("GENERATED from Auth_UI/apps/accounts/src/pages/privacy/content",
            "that bundle is gone; privacy-policy-absence.test.ts keeps it gone");
        seed.Should().Contain("new installations only");
    }

    private static Dictionary<string, JsonElement> Documents()
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath()),
            @"@VersionId, N'(?<lang>[a-z]{2})', N'(?<json>\{(?:[^']|'')*\})', GETUTCDATE\(\)");

        rows.Should().HaveCount(Languages.Supported.Count);
        return rows.ToDictionary(
            row => row.Groups["lang"].Value,
            row => JsonDocument.Parse(row.Groups["json"].Value.Replace("''", "'")).RootElement.Clone());
    }

    private static string SeedPath() =>
        Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", Seed);
}
