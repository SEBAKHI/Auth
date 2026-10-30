using Auth.Application.Configuration;
using Auth.Domain.Services;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth.Sdk.Validation;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// The API key format of OI-128 PR B (<see cref="ApiKeyFormat"/>):
/// <c>ak_&lt;env&gt;_&lt;selector&gt;_&lt;secret&gt;&lt;check&gt;</c>. Minted by the real generator and hashed by the
/// real Argon2id hasher, so the rollback rule is proven on what a deployment would store.
/// </summary>
public class ApiKeyFormatTests
{
    private static readonly Argon2PasswordHasher Hasher = new(Options.Create(new PasswordSettings
    {
        Argon2MemorySize = 19456,
        Argon2Iterations = 2,
        Argon2Parallelism = 1,
        SaltSize = 16,
        HashSize = 32,
    }));

    private static readonly ApiKeyGenerator Generator = new(Hasher);

    public static TheoryData<string, string> Environments => new()
    {
        { "production", "ak_prod_" },
        { "staging", "ak_stag_" },
        { "development", "ak_dev_" },
        { "Production", "ak_prod_" },
        { "qa-eu", "ak_othr_" },
        { "preview", "ak_othr_" },
    };

    [Fact]
    public void Environments_IsNotEmpty() => Environments.Count<object[]>().Should().Be(6);

    [Theory]
    [MemberData(nameof(Environments))]
    public void Generate_MintsTheSelectorFormat_WithOneEnvironmentSegment(string environment, string prefix)
    {
        var key = Generator.Generate(environment);

        key.KeyPrefix.Should().Be(prefix);
        key.KeyPrefix.Length.Should().BeLessThanOrEqualTo(10, "ApiKeys.KeyPrefix is NVARCHAR(10)");
        key.PlainKey.Should().StartWith(prefix + key.KeySelector + "_");
        key.KeySelector.Should().HaveLength(ApiKeyFormat.SelectorLength).And.MatchRegex("^[0-9A-Za-z]+$");
        key.PlainKey.Length.Should().Be(prefix.Length + 16 + 1 + 32 + 6);
        key.ToString().Should().NotContain(key.PlainKey, "a logged record must not carry the plain key");

        ApiKeyFormat.TryParse(key.PlainKey, out var parsedPrefix, out var selector).Should().BeTrue();
        parsedPrefix.Should().Be(prefix);
        selector.Should().Be(key.KeySelector);
        ApiKeyShape.IsWellFormed(key.PlainKey).Should().BeTrue("the SDK's offline check and the server's agree");
    }

    /// <summary>
    /// Rollback (B4): the validation before PR B took the text up to the second underscore as the
    /// prefix, loaded every active key with that prefix, and verified the whole key against each
    /// hash. A key PR B mints must pass exactly that, so a revert keeps every new key working.
    /// </summary>
    [Theory]
    [InlineData("production")]
    [InlineData("qa-eu")]
    public void AKeyThisFormatMints_PassesThePrePrBExtractionAndVerify(string environment)
    {
        var key = Generator.Generate(environment);

        var legacyPrefix = PrePrBExtractPrefix(key.PlainKey);

        legacyPrefix.Should().Be(key.KeyPrefix, "the stored prefix is what the older API looks keys up by");
        Hasher.VerifyPassword(key.PlainKey, key.KeyHash).Should().BeTrue("the stored hash covers the whole key");
    }

    [Fact]
    public void EverySingleCharacterChange_IsCaughtByTheCheck()
    {
        var key = Generator.Generate("production").PlainKey;
        var caught = 0;

        for (var i = 0; i < key.Length; i++)
        {
            if (key[i] == '_')
            {
                continue;
            }

            var replacement = key[i] == 'A' ? 'B' : 'A';
            var tampered = key[..i] + replacement + key[(i + 1)..];

            // A changed environment segment is caught by the shape; any other change by the check.
            ApiKeyFormat.TryParse(tampered, out _, out _).Should().BeFalse($"position {i}");
            ApiKeyShape.IsWellFormed(tampered).Should().BeFalse($"position {i} (SDK)");
            caught++;
        }

        caught.Should().BeGreaterThan(55);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ak_prod_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("ak_prod_Sel3ctor0000000A_short")]
    [InlineData("ak_test_Sel3ctor0000000A_ssssssssssssssssssssssssssssssss000000")]
    [InlineData("wk_prod_Sel3ctor0000000A_ssssssssssssssssssssssssssssssss000000")]
    [InlineData("ak_prod_Sel3ctor000000+A_ssssssssssssssssssssssssssssssss000000")]
    public void MalformedOrLegacyKeys_AreNotParsed(string? raw)
    {
        ApiKeyFormat.TryParse(raw, out _, out _).Should().BeFalse();
        ApiKeyShape.IsWellFormed(raw).Should().BeFalse();
    }

    [Fact]
    public void ManyGeneratedKeys_AreAllWellFormed_AndUnique()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => Generator.Generate("development")).ToList();

        keys.Where(k => !ApiKeyFormat.TryParse(k.PlainKey, out _, out _) || !ApiKeyShape.IsWellFormed(k.PlainKey))
            .Should().BeEmpty();
        keys.Select(k => k.KeySelector).Should().OnlyHaveUniqueItems();
    }

    /// <summary>ValidateApiKeyQueryHandler.ExtractPrefix before OI-128 PR B, verbatim in behaviour.</summary>
    private static string? PrePrBExtractPrefix(string rawApiKey)
    {
        var firstUnderscore = rawApiKey.IndexOf('_');
        if (firstUnderscore < 0)
            return null;

        var secondUnderscore = rawApiKey.IndexOf('_', firstUnderscore + 1);
        if (secondUnderscore < 0)
        {
            return rawApiKey[..(firstUnderscore + 1)];
        }

        return rawApiKey[..(secondUnderscore + 1)];
    }
}
