using System.Text.RegularExpressions;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS3 (KR2): every bounded-lifetime fingerprint keeps the shape it has today,
/// 44 base64 characters ending in '=', under the legacy form and under a subkey
/// alike — the refresh-token procedure takes NVARCHAR(100) and a pending
/// registration's handle column is NVARCHAR(100), so a longer value would be cut
/// or refused. Only webhook keys carry a key-id prefix, and it fits their column.
/// </summary>
/// <remarks>
/// The card's third assertion, "the invitation script's predicate is constant", has
/// nothing left to pin: the upgrade script was deleted with every upgrade script in
/// #55 (b444749b) and no post-deploy step touches invitations (deviation row, S11).
/// </remarks>
public class IndexHashShapeGuardTests
{
    private static readonly Regex Base64Of32Bytes = new("^[A-Za-z0-9+/]{43}=$", RegexOptions.Compiled);

    public static TheoryData<CredentialPurpose> BoundedPurposes()
    {
        var data = new TheoryData<CredentialPurpose>();
        foreach (var purpose in Enum.GetValues<CredentialPurpose>().Where(p => p != CredentialPurpose.WebhookKey))
        {
            data.Add(purpose);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BoundedPurposes))]
    public void ABoundedFingerprint_Is44Base64Characters_InBothForms(CredentialPurpose purpose)
    {
        var legacy = TestCredentialHashers.Over(KeyringTestRings.SingleKey()).Hash(purpose, "a-presented-credential");
        var subkey = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover))
            .Hash(purpose, "a-presented-credential");

        legacy.Should().MatchRegex(Base64Of32Bytes);
        subkey.Should().MatchRegex(Base64Of32Bytes);
        subkey.Should().NotBe(legacy);
    }

    [Fact]
    public void AWebhookFingerprint_FitsItsColumn_WhateverTheKeyId()
    {
        var column = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "WebhookKeys.sql"));
        var width = int.Parse(Regex.Match(column, @"\[KeyHash\]\s+NVARCHAR\((\d+)\)").Groups[1].Value);

        var ring = HmacKeyring.Create(
            [new HmacKey(1, KeyringTestRings.Key1Material, demotedAtUtc: KeyringTestRings.Cutover),
             new HmacKey(int.MaxValue, KeyringTestRings.Key2Material)],
            currentKeyId: int.MaxValue, nextKeyId: null, legacyCutoverUtc: KeyringTestRings.Cutover, legacyVerifyUntilUtc: null).Value;

        var stored = TestCredentialHashers.Over(ring).Hash(CredentialPurpose.WebhookKey, "wk_live_sample");

        stored.Length.Should().BeLessThanOrEqualTo(width);
        stored.Should().StartWith($"hk{int.MaxValue}$");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }
}
