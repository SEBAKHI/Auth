using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the two-factor-changed seed (P3 deploy-1 batch) to the code that will send it, and
/// proves that its Liquid logic renders.
///
/// It is the first seeded template that branches: one type for every kind of change, with the
/// heading and the explanation chosen by <c>{% case ChangeKind %}</c> and the device line printed
/// by <c>{% if %}</c>. Nothing at deploy time renders a seed: the version is inserted with its
/// publish pointer already set and never passes the console's publish gate, and the preview
/// renders the sample data, which holds one kind. A misspelt <c>when</c> value or an unclosed
/// tag would surface as an empty heading in a security notice, in production, for the kinds
/// nobody previewed. So every kind is rendered here, in every language, through the real
/// renderer.
/// </summary>
public class TwoFactorChangedSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TypeId = "40000000-0000-0000-0000-000000000021";
    private const string TemplateId = "42000000-0000-0000-0000-000000000021";
    private const string Version1 = "43000000-0000-0000-0000-000000000021";
    private const string Version2 = "43000000-0000-0000-0002-000000000021";
    private const string Version3 = "43000000-0000-0000-0003-000000000021";

    /// <summary>The kinds version 1 words, each with a word its English heading must carry.</summary>
    private static readonly (string Kind, string EnglishHeadingWord)[] Version1Kinds =
    [
        ("enabled", "turned on"),
        ("disabled", "turned off"),
        ("recovery-codes-regenerated", "recovery codes"),
        ("authenticator-replaced", "authenticator app"),
        ("reset-by-administrator", "administrator"),
    ];

    /// <summary>Version 2 (S24) words version 1's kinds and the emailed code's three.</summary>
    private static readonly (string Kind, string EnglishHeadingWord)[] Version2Kinds =
    [
        .. Version1Kinds,
        ("email-code-enabled", "codes by email were turned on"),
        ("email-code-disabled", "codes by email were turned off"),
        ("authenticator-replaced-email-code", "replaced your codes by email"),
    ];

    /// <summary>
    /// The passkey-removed kinds of the removals the system makes by itself (REVIEW 150 F12):
    /// each names the passkey, says why, and replaces the closing advice with what to do.
    /// </summary>
    private static readonly (string Kind, string EnglishHeadingWord)[] SystemRemovalKinds =
    [
        ("passkey-removed-password-change", "password was changed"),
        ("passkey-removed-password-reset", "reset link"),
        ("passkey-removed-admin-lock", "account was locked"),
    ];

    /// <summary>Version 3 (S20) words version 2's kinds and the passkey's five.</summary>
    private static readonly (string Kind, string EnglishHeadingWord)[] Version3Kinds =
    [
        .. Version2Kinds,
        ("passkey-registered", "passkey was added"),
        ("passkey-removed", "passkey was removed"),
        .. SystemRemovalKinds,
    ];

    /// <summary>Every seeded version: the published one is the newest, so each must render.</summary>
    public static TheoryData<string> Versions => new() { Version1, Version2, Version3 };

    /// <summary>Every kind of every version, with the word its English heading must carry.</summary>
    public static TheoryData<string, string, string> VersionKinds
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var version in new[] { Version1, Version2, Version3 })
            {
                foreach (var (kind, word) in KindsOf(version))
                {
                    data.Add(version, kind, word);
                }
            }

            return data;
        }
    }

    private static readonly string[] Catalog =
        ["UserName", "ChangeKind", "OccurredAtUtc", "DeviceName", "ManageSecurityLink", "PasskeyName", "PasskeyAddedAt", "PasskeysRemoved"];

    /// <summary>
    /// What a sender may leave out: the device, the passkey a kind other than a removal does not
    /// name, and the count only an administrator's reset passes (REVIEW-FIXES 150 F16).
    /// </summary>
    private static readonly string[] OptionalVariables = ["DeviceName", "PasskeyName", "PasskeyAddedAt", "PasskeysRemoved"];

    private readonly FluidTemplateRenderer _renderer = new(TimeProvider.System);

    [Fact]
    public void TheType_IsASystemType_SeededInsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        TypeBlock(seed, TypeId).Should().Contain($"N'{NotificationTypeCodes.TwoFactorChanged}'");
        TypeBlock(seed, TypeId).Should().MatchRegex(@"N'[^']*',\s*1,\s*N'\[",
            "IsSystem = 1 is what the template guards read: a system template cannot be unpublished or deleted");
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorChanged);

        // 10_ declares @SystemUserId once; a block after its only GO is Msg 137 at publish time.
        var goes = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline);
        goes.Should().HaveCount(1, "10_ is one batch by design");
        seed.IndexOf(TypeId, StringComparison.Ordinal).Should().BeLessThan(goes.Single().Index);
    }

    [Fact]
    public void TheCatalog_IsWhatTheSenderPasses_WithTheDeviceAndThePasskeyOptional()
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), TypeId);
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        var variables = JsonDocument.Parse(variablesJson).RootElement.EnumerateArray()
            .ToDictionary(
                variable => variable.GetProperty("name").GetString()!,
                variable => variable.GetProperty("required").GetBoolean());

        variables.Keys.Should().BeEquivalentTo(Catalog,
            "the catalog is the contract the sender fills; a variable in one and not the other renders blank");
        variables.Where(pair => !pair.Value).Select(pair => pair.Key).Should().Equal(OptionalVariables,
            "an administrator's reset has no device of the owner's to name, and only a passkey removal names a passkey; everything else is always known");

        // The publish gate and the console preview render the sample data, so it must hold
        // every variable the template names, the optional ones included.
        SampleModel(block).Keys.Should().BeEquivalentTo(Catalog);
    }

    [Fact]
    public void ADatabaseThatHoldsTheType_GainsThePasskeyVariables_ByAnIdempotentUpdate()
    {
        // REVIEW 150 F12, S24's way: the guarded insert carries the new catalog for a fresh
        // database, and an update gives it to one that already holds the type, guarded by the
        // newest of the variables, PasskeysRemoved (REVIEW-FIXES 150 F16).
        var seed = File.ReadAllText(SeedPath(TypesSeed)).Replace("\r\n", "\n");
        var insert = Regex.Match(seed,
            @"'40000000-0000-0000-0000-000000000021',\s+N'two-factor-changed',.*?N'(?<vars>\[\{""name"".*?\])',\s+N'(?<sample>\{.*?\})',",
            RegexOptions.Singleline);
        var update = Regex.Match(seed,
            @"UPDATE \[dbo\]\.\[NotificationTypes\]\s+SET \[VariablesJson\] = N'(?<vars>\[[^']*\])',\s+\[SampleDataJson\] = N'(?<sample>\{[^']*\})'\s+"
            + @"WHERE \[Id\] = '40000000-0000-0000-0000-000000000021'\s+AND \[VariablesJson\] NOT LIKE N'%""name"":""PasskeysRemoved""%';");

        insert.Success.Should().BeTrue();
        update.Success.Should().BeTrue("the guarded insert never reaches a database that already holds the type");
        update.Groups["vars"].Value.Should().Be(insert.Groups["vars"].Value, "the update writes the catalog a fresh database gets");
        update.Groups["sample"].Value.Should().Be(insert.Groups["sample"].Value);
        update.Index.Should().BeLessThan(Regex.Match(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Index, "inside the seed's single batch");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryTranslation_UsesOnlyCatalogedVariables(string versionId)
    {
        foreach (var (language, subject, body) in Translations(versionId))
        {
            // Output tags with or without a filter, and the tags that read a variable.
            var used = Regex.Matches(subject + body, @"\{\{-?\s*([A-Za-z_][\w.]*)|\{%-?\s*(?:case|if|elsif|unless)\s+([A-Za-z_]\w*)")
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            used.Should().BeSubsetOf(Catalog, $"the {language} translation names only cataloged variables");
            used.Should().Contain(["ChangeKind", "OccurredAtUtc", "ManageSecurityLink", "UserName"],
                $"the {language} translation says what changed, when, to whom, and where to review it");
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryTranslation_WordsExactlyTheSeededKinds_InItsHeadingAndExplanation(string versionId)
    {
        // The theory data below drives the renders; this pins the seed to it. A kind added to
        // a version by a later item (S20, S24 ship theirs in versions of their own) or a kind
        // dropped from one branch would otherwise pass every render.
        var kinds = KindsOf(versionId).Select(row => row.Kind).ToList();

        // Version 3 branches a third time, in the closing advice, for the removals the system
        // made by itself (REVIEW 150 F12): a heading, an explanation and what to do.
        var branches = versionId == Version3 ? 3 : 2;
        var systemRemovals = versionId == Version3 ? SystemRemovalKinds.Select(row => row.Kind).ToHashSet() : new HashSet<string>();

        foreach (var (language, subject, body) in Translations(versionId))
        {
            subject.Should().NotContain("{%", $"the {language} subject is fixed; only the body branches");
            Regex.Matches(body, @"\{%-?\s*case\s+ChangeKind\s*-?%\}").Should().HaveCount(branches,
                $"the {language} body branches on the kind in the heading, the explanation{(branches == 3 ? " and the closing advice" : "")}");
            Regex.Matches(body, @"\{%-?\s*else\s*-?%\}").Should().HaveCount(branches,
                $"every {language} branch falls back to generic wording for a kind added later");

            var whens = Regex.Matches(body, @"\{%-?\s*when\s+""([^""]*)""\s*-?%\}")
                .Select(match => match.Groups[1].Value)
                .ToList();

            whens.Distinct().Should().BeEquivalentTo(kinds, $"the {language} body words exactly the seeded kinds");
            whens.GroupBy(kind => kind).Should().OnlyContain(group => group.Count() == (systemRemovals.Contains(group.Key) ? 3 : 2),
                $"each kind has a {language} heading and a {language} explanation, and a removal the system made its own advice");
        }
    }

    [Theory]
    [MemberData(nameof(VersionKinds))]
    public void EveryKind_RendersInEveryLanguage_WithNothingUnresolved(string versionId, string kind, string englishHeadingWord)
    {
        var translations = Translations(versionId);
        translations.Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);

        foreach (var (language, subjectSource, bodySource) in translations)
        {
            var model = Model(kind, deviceName: "Chrome on Windows");
            var subject = _renderer.RenderTracking(subjectSource, model, language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, model, language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse($"the {language} subject must render: {(subject.IsError ? subject.FirstError.Description : "")}");
            body.IsError.Should().BeFalse($"the {language} body must render: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names nothing the sender will not pass");

            body.Value.Should().NotContain("{{").And.NotContain("{%").And.NotContain("%}");
            Heading(body.Value).Should().NotBeNullOrWhiteSpace($"the {language} heading for {kind} must not be empty");
            body.Value.Should().Contain("Example Platform")
                .And.Contain("2026-09-19 09:14:00Z")
                .And.Contain("Chrome on Windows")
                .And.Contain("href=\"https://example.com/profile?tab=security\"");

            if (language == "en")
            {
                Heading(body.Value).Should().ContainEquivalentOf(englishHeadingWord, $"the {kind} heading names the change");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EachKind_HasItsOwnWording_InEveryLanguage_AndAnUnknownKindFallsBackToGenericWording(string versionId)
    {
        var kinds = KindsOf(versionId).Select(row => row.Kind).Append("a-kind-added-later").ToList();

        foreach (var (language, _, bodySource) in Translations(versionId))
        {
            var bodies = new List<string>();
            foreach (var kind in kinds)
            {
                // Tracked, so the else-branch (the unknown kind) is held to the same catalog.
                var body = _renderer.RenderTracking(bodySource, Model(kind, "Chrome on Windows"), language, encodeHtml: true, out var missing);
                body.IsError.Should().BeFalse();
                missing.Should().BeEmpty($"the {language} wording for {kind} names nothing the sender will not pass");
                bodies.Add(body.Value);
            }

            bodies.Select(Heading).Should().OnlyHaveUniqueItems(
                $"each {language} heading must say which change happened; a copy-pasted branch tells the owner the wrong thing");
            bodies.Select(Explanation).Should().OnlyHaveUniqueItems($"each {language} explanation must differ per kind");
            bodies.Select(Heading).Should().AllSatisfy(heading => heading.Should().NotBeNullOrWhiteSpace(
                $"a kind this version does not know must fall to the {language} else-wording, not to an empty heading"));
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheDeviceLine_IsPrintedOnlyWhenDeviceNameHasAValue(string versionId)
    {
        foreach (var (language, _, bodySource) in Translations(versionId))
        {
            var withDevice = _renderer.Render(bodySource, Model("disabled", "Chrome on Windows"), language, encodeHtml: true).Value;
            var withNull = _renderer.Render(bodySource, Model("disabled", null), language, encodeHtml: true).Value;
            var withEmpty = _renderer.Render(bodySource, Model("disabled", ""), language, encodeHtml: true).Value;
            var absent = Model("disabled", null).Where(pair => pair.Key != "DeviceName").ToDictionary();
            var withoutKey = _renderer.RenderTracking(bodySource, absent, language, encodeHtml: true, out var missing).Value;

            Notice(withDevice).Should().Contain("<br />").And.Contain("Chrome on Windows",
                $"with a device the {language} notice names it on its own line");
            foreach (var body in new[] { withNull, withEmpty, withoutKey })
            {
                Notice(body).Should().NotContain("<br />",
                    $"without a device the {language} notice must not print a device label with nothing after it");
                Notice(body).Should().Contain("2026-09-19 09:14:00Z");
            }

            missing.Should().Equal(["DeviceName"], "DeviceName is the only variable a sender may leave out");
        }
    }

    [Fact]
    public void Version2_IsANewVersionPublishedOnlyWhileTheSeededVersion1IsThePublishedOne()
    {
        // S24: a version an administrator wrote and published stays published; the
        // version number is the next free one, so it never collides with theirs.
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var start = seed.IndexOf($"AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '{Version2}')", StringComparison.Ordinal);
        start.Should().BePositive("version 2 is inserted once, guarded by its own id");
        var block = seed[start..seed.IndexOf("\nEND", start, StringComparison.Ordinal)];

        block.Should().Contain("ISNULL(MAX([VersionNumber]), 0) + 1");
        Regex.Replace(block, @"\s+", " ").Should().Contain(
            $"UPDATE [dbo].[NotificationTemplates] SET [PublishedVersionId] = '{Version2}' WHERE [Id] = '{TemplateId}' "
            + $"AND [PublishedVersionId] = '{Version1}';");
    }

    private static (string Kind, string EnglishHeadingWord)[] KindsOf(string versionId) =>
        versionId == Version3 ? Version3Kinds : versionId == Version2 ? Version2Kinds : Version1Kinds;

    [Fact]
    public void Version3_IsANewVersionPublishedOnlyWhileTheSeededVersion2IsThePublishedOne()
    {
        // S20: as version 2 did for version 1 — an administrator's own version stays published.
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var start = seed.IndexOf($"AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '{Version3}')", StringComparison.Ordinal);
        start.Should().BePositive("version 3 is inserted once, guarded by its own id");
        var block = seed[start..seed.IndexOf("\nEND", start, StringComparison.Ordinal)];

        block.Should().Contain("ISNULL(MAX([VersionNumber]), 0) + 1");
        Regex.Replace(block, @"\s+", " ").Should().Contain(
            $"UPDATE [dbo].[NotificationTemplates] SET [PublishedVersionId] = '{Version3}' WHERE [Id] = '{TemplateId}' "
            + $"AND [PublishedVersionId] = '{Version2}';");
    }

    private static IReadOnlyDictionary<string, object?> Model(string kind, string? deviceName)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = "Jane Doe",
            ["ChangeKind"] = kind,
            ["OccurredAtUtc"] = "2026-09-19 09:14:00Z",
            ["DeviceName"] = deviceName,
            ["ManageSecurityLink"] = "https://example.com/profile?tab=security",
            // Injected by the rendering service into every render.
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

        // The sender passes the passkey's name and date with a passkey removal only.
        if (kind.StartsWith("passkey-removed", StringComparison.Ordinal))
        {
            model["PasskeyName"] = "Work <laptop> & co";
            model["PasskeyAddedAt"] = "2026-09-17 08:00:00Z";
        }

        // And an administrator's reset passes how many passkeys went with the factor (F16).
        if (kind == "reset-by-administrator")
        {
            model["PasskeysRemoved"] = 0;
        }

        return model;
    }

    /// <summary>The paragraph after the notice: the closing advice.</summary>
    private static string Closing(string body) =>
        Regex.Matches(body, "<p class=\"message\">(.*?)</p>", RegexOptions.Singleline)[2].Groups[1].Value;

    /// <summary>
    /// REVIEW 150 F12: a passkey the system revoked by itself — a password change, a reset, an
    /// administrator's lock — is told to its owner in words of its reason: which passkey and
    /// when it was added, why (added in the 72 hours before; a passkey that new may be someone
    /// else's), and what to do, in place of the "change your password now" a person who just
    /// changed it, or cannot sign in under a lock, cannot act on.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the closing advice's branch dropped (the generic words back).</remarks>
    [Fact]
    public void TheSystemsOwnRemovals_NameThePasskey_SayWhy_AndWhatToDo_InEveryLanguage()
    {
        foreach (var (language, _, bodySource) in Translations(Version3))
        {
            var generic = Closing(_renderer.Render(bodySource, Model("disabled", null), language, encodeHtml: true).Value);

            foreach (var (kind, _) in SystemRemovalKinds)
            {
                var body = _renderer.RenderTracking(bodySource, Model(kind, null), language, encodeHtml: true, out var missing).Value;

                missing.Should().BeEmpty($"the {language} {kind} wording names nothing the sender does not pass");
                Explanation(body).Should().Contain("Work &lt;laptop&gt; &amp; co", $"the {language} notice names the passkey, encoded")
                    .And.Contain("2026-09-17 08:00:00Z", $"the {language} notice says when it was added")
                    .And.Contain("72", $"the {language} notice says why: it was added in the 72 hours before");
                Closing(body).Should().NotBe(generic, $"the {language} {kind} notice says what to do, not the generic advice");
            }
        }

        var english = Translations(Version3).Single(row => row.Language == "en").Body;
        foreach (var (kind, _) in SystemRemovalKinds)
        {
            var closing = Closing(_renderer.Render(english, Model(kind, null), "en", encodeHtml: true).Value);
            closing.Should().NotContain("Change your password now");
            closing.Should().Contain(kind.EndsWith("admin-lock", StringComparison.Ordinal)
                ? "add it again once the administrator lifts the lock"
                : "add it again once you are signed in");
            closing.Should().Contain(kind.EndsWith("admin-lock", StringComparison.Ordinal)
                ? "contact your administrator"
                : "review your security settings");
        }
    }

    /// <summary>
    /// REVIEW 150 F12: the owner's own removal keeps the words version 3 was written with — no
    /// name, no date, and the generic closing advice — in every language (REVIEW-FIXES 150 F18 (a)).
    /// </summary>
    /// <remarks>Deliberate break, verified red: one language's wording of the owner's removal changed.</remarks>
    [Fact]
    public void TheOwnersOwnRemoval_KeepsItsWords()
    {
        foreach (var (language, _, bodySource) in Translations(Version3))
        {
            var own = _renderer.Render(bodySource, Model("passkey-removed", "Chrome on Windows"), language, encodeHtml: true).Value;
            var generic = _renderer.Render(bodySource, Model("disabled", "Chrome on Windows"), language, encodeHtml: true).Value;

            Closing(own).Should().Be(Closing(generic), $"the {language} owner's removal keeps the generic closing advice");
            Explanation(own).Should().NotContain("Work &lt;laptop&gt;").And.NotContain("2026-09-17");

            var words = _renderer.Render(bodySource, Model("passkey-removed", null), language, encodeHtml: true).Value;
            Heading(words).Should().Be(OwnersOwnRemovalWords[language].Heading, $"the {language} heading keeps its words");
            Explanation(words).Should().Be(OwnersOwnRemovalWords[language].Explanation, $"the {language} explanation keeps its words");
        }
    }

    /// <summary>The owner's own removal, as version 3 words it: heading and explanation per language.</summary>
    private static readonly IReadOnlyDictionary<string, (string Heading, string Explanation)> OwnersOwnRemovalWords =
        new Dictionary<string, (string Heading, string Explanation)>(StringComparer.Ordinal)
    {
        ["en"] = ("A passkey was removed from your account",
            "A passkey was just removed from your Example Platform account. It no longer signs in, and your other sessions were signed out."),
        ["ar"] = ("أُزيل مفتاح مرور من حسابك",
            "أُزيل للتوّ مفتاح مرور من حسابك على Example Platform. لم يعد يسجّل الدخول، وأُنهيت جلساتك الأخرى."),
        ["tr"] = ("Hesabınızdan bir geçiş anahtarı kaldırıldı",
            "Example Platform hesabınızdan az önce bir geçiş anahtarı kaldırıldı. Artık giriş yapamaz ve diğer oturumlarınız kapatıldı."),
        ["fr"] = ("Une clé d'accès a été supprimée de votre compte",
            "Une clé d'accès vient d'être supprimée de votre compte Example Platform. Elle ne permet plus de se connecter, et vos autres sessions ont été fermées."),
        ["zh"] = ("您的账户移除了一个通行密钥",
            "您的 Example Platform 账户刚刚移除了一个通行密钥。它已无法再登录，您的其他会话也已退出。"),
        ["ur"] = ("آپ کے اکاؤنٹ سے ایک پاس کی ہٹا دی گئی",
            "آپ کے Example Platform اکاؤنٹ سے ابھی ایک پاس کی ہٹا دی گئی ہے۔ اب یہ سائن اِن نہیں کر سکتی، اور آپ کے دوسرے سیشن ختم کر دیے گئے ہیں۔"),
        ["fa"] = ("یک کلید عبور از حساب شما حذف شد",
            "همین حالا یک کلید عبور از حساب Example Platform شما حذف شد. دیگر با آن نمی\u200cتوان وارد شد و نشست\u200cهای دیگر شما بسته شدند."),
    };

    /// <summary>
    /// REVIEW-FIXES 150 F16: an administrator's reset revokes every passkey, and its notice says
    /// so only when there were some — they no longer sign in, and go back once two-factor
    /// authentication is set up — while with none, or a sender that passes no count, the words
    /// are version 2's exactly.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the condition dropped (the sentence printed with 0).</remarks>
    [Fact]
    public void TheAdministratorsReset_SaysThePasskeysWentToo_OnlyWhenThereWereSome()
    {
        var version2 = Translations(Version2).ToDictionary(row => row.Language, row => row.Body, StringComparer.Ordinal);
        foreach (var (language, _, bodySource) in Translations(Version3))
        {
            var before = Model("reset-by-administrator", null).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            before.Remove("PasskeysRemoved");
            var words = Explanation(_renderer.Render(version2[language], before, language, encodeHtml: true).Value);

            var none = Explanation(_renderer.Render(bodySource, WithPasskeysRemoved(0), language, encodeHtml: true).Value);
            var absent = Explanation(_renderer.Render(bodySource, before, language, encodeHtml: true).Value);
            var two = Explanation(_renderer.Render(bodySource, WithPasskeysRemoved(2), language, encodeHtml: true).Value);

            none.Should().Be(words, $"the {language} notice of a reset that removed no passkey is version 2's");
            absent.Should().Be(words, $"the {language} notice from a sender that passes no count is version 2's");
            two.Should().StartWith(words, $"the {language} notice keeps version 2's words")
                .And.Contain(PasskeysWord[language], $"the {language} notice says the passkeys went too");
            two.Length.Should().BeGreaterThan(words.Length);
        }

        static IReadOnlyDictionary<string, object?> WithPasskeysRemoved(int count)
        {
            var model = Model("reset-by-administrator", null).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            model["PasskeysRemoved"] = count;
            return model;
        }
    }

    /// <summary>The word for the account's passkeys, as each language's reset sentence writes it.</summary>
    private static readonly IReadOnlyDictionary<string, string> PasskeysWord = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "passkeys",
        ["ar"] = "مفاتيح المرور",
        ["tr"] = "geçiş anahtarları",
        ["fr"] = "clés d'accès",
        ["zh"] = "通行密钥",
        ["ur"] = "پاس کیز",
        ["fa"] = "کلیدهای عبور",
    };

    /// <summary>
    /// REVIEW-FIXES 150 F18 (b): a removal the system made names the passkey and its date only
    /// when the sender passed them, so a render without them shows no empty quotes and no
    /// dangling "added".
    /// </summary>
    /// <remarks>Deliberate break, verified red: one language's name printed unconditionally.</remarks>
    [Fact]
    public void TheSystemsOwnRemovals_WithoutTheNameOrTheDate_ShowNoEmptyQuotes()
    {
        string[] emptyMarks = ["“”", "«»", "« »", "\"\"", "()", "،،", "، ،", ",,", ", ,", "  "];
        foreach (var (language, _, bodySource) in Translations(Version3))
        {
            foreach (var (kind, _) in SystemRemovalKinds)
            {
                foreach (var (name, addedAt) in new (string?, string?)[] { (null, null), ("Laptop", null), (null, "2026-09-17 08:00:00Z") })
                {
                    var model = Model(kind, null).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    model["PasskeyName"] = name;
                    model["PasskeyAddedAt"] = addedAt;

                    var explanation = Explanation(_renderer.Render(bodySource, model, language, encodeHtml: true).Value);

                    foreach (var mark in emptyMarks)
                    {
                        explanation.Should().NotContain(mark, $"the {language} {kind} notice with name {name ?? "absent"} and date {addedAt ?? "absent"}");
                    }

                    if (addedAt is null)
                    {
                        explanation.Should().NotContain(" added ,").And.NotContain("ajoutée le ,");
                    }
                }
            }
        }
    }

    /// <summary>
    /// REVIEW-FIXES 150 F18 (d): a passkey removed by a reset may follow a first password a reset
    /// link set — the account had none — so the wording says a password was set with a reset link,
    /// true in both cases, in every language.
    /// </summary>
    /// <remarks>Deliberate break, verified red: one language's old "your password was reset" restored.</remarks>
    [Fact]
    public void ThePasswordResetWording_IsTrueForAFirstPasswordSetByALink_InEveryLanguage()
    {
        foreach (var (language, _, bodySource) in Translations(Version3))
        {
            var body = _renderer.Render(bodySource, Model("passkey-removed-password-reset", null), language, encodeHtml: true).Value;

            Heading(body).Should().Contain(ResetLinkWord[language], $"the {language} heading names the reset link");
            Explanation(body).Should().Contain(ResetLinkWord[language], $"the {language} explanation names the reset link");
        }

        var english = _renderer.Render(
            Translations(Version3).Single(row => row.Language == "en").Body, Model("passkey-removed-password-reset", null), "en", encodeHtml: true).Value;
        Explanation(english).Should().NotContain("its password was reset", "an account a reset link gave its first password had none to reset");
    }

    /// <summary>The words for a reset link, as each language's password-reset wording writes them.</summary>
    private static readonly IReadOnlyDictionary<string, string> ResetLinkWord = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "reset link",
        ["ar"] = "برابط إعادة التعيين",
        ["tr"] = "ıfırlama bağlantısıyla",
        ["fr"] = "lien de réinitialisation",
        ["zh"] = "重置链接",
        ["ur"] = "ری سیٹ لنک",
        ["fa"] = "پیوند بازنشانی",
    };

    private static string Heading(string body) =>
        Regex.Match(body, "<h1>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value.Trim();

    /// <summary>The paragraph after the greeting: the one that explains the change.</summary>
    private static string Explanation(string body) =>
        Regex.Matches(body, "<p class=\"message\">(.*?)</p>", RegexOptions.Singleline)[1].Groups[1].Value;

    private static string Notice(string body) =>
        Regex.Match(body, "<p class=\"notice-text\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;

    /// <summary>Language, subject source and body source of every translation of one version.</summary>
    private static List<(string Language, string Subject, string Body)> Translations(string versionId)
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-000\d-0021-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["lang"].Value,
                row.Groups["subject"].Value.Replace("''", "'"),
                row.Groups["body"].Value.Replace("''", "'")))
            .ToList();

        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");
        return rows;
    }

    /// <summary>The seeded sample data, the model the console preview and the publish gate render.</summary>
    private static IReadOnlyDictionary<string, object?> SampleModel(string typeBlock)
    {
        var sampleJson = Regex.Match(typeBlock, @"N'(\{""[^']*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        return JsonDocument.Parse(sampleJson).RootElement.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetInt32() : (object?)property.Value.GetString(),
                StringComparer.Ordinal);
    }

    /// <summary>The guarded INSERT block for one type id, from its IF NOT EXISTS to its END.</summary>
    private static string TypeBlock(string seed, string typeId)
    {
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{typeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{typeId} needs a guarded insert");
        var end = seed.IndexOf("\nEND", start, StringComparison.Ordinal);
        return seed[start..end];
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
