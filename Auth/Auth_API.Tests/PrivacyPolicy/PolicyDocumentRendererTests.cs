using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Infrastructure.PrivacyPolicy;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// The renderer produces the exact bytes the public is served, so these tests
/// are the last check before a legal document reaches a reader.
/// </summary>
public class PolicyDocumentRendererTests
{
    private static readonly PolicyDocumentRenderer Renderer = new();

    [Fact]
    public void EveryAuthoredField_ReachesTheRenderedDocument()
    {
        // The anti-drift guard. A field added to the document contract but not
        // to the renderer would otherwise disappear from a published legal
        // notice in silence, which no compiler or type check would catch.
        var result = Renderer.Render(Request(Sentinels()));

        result.IsError.Should().BeFalse();
        foreach (var sentinel in SentinelValues)
        {
            result.Value.Html.Should().Contain(sentinel,
                $"'{sentinel}' is authored content and must appear in the served document");
        }
    }

    [Fact]
    public void AnUnresolvedToken_FailsTheRenderRatherThanReachingTheReader()
    {
        var content = Sentinels();
        content.Intro = ["We are {{legalName}} of {{unknownToken}}."];

        var result = Renderer.Render(Request(content));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        result.FirstError.Description.Should().Contain("unknownToken");
    }

    [Fact]
    public void ABlankDisclosureValue_CountsAsUnresolved()
    {
        // The silent version of the same defect: a required disclosure that
        // renders as an empty gap reads as a finished sentence with a hole in it.
        var content = Sentinels();
        content.Intro = ["Contact {{privacyEmail}}."];

        var disclosure = Disclosure();
        disclosure.PrivacyEmail = string.Empty;

        var result = Renderer.Render(Request(content, disclosure));

        result.IsError.Should().BeTrue();
        result.FirstError.Description.Should().Contain("privacyEmail");
    }

    [Fact]
    public void TokensAreResolvedFromTheDisclosure()
    {
        var content = Sentinels();
        content.Intro = ["{{legalName}} keeps login records for {{loginAttemptRetentionDays}} days."];

        var result = Renderer.Render(Request(content));

        result.IsError.Should().BeFalse();
        result.Value.Html.Should().Contain("Acme Corp LLC keeps login records for 365 days.");
        result.Value.Html.Should().NotContain("{{");
    }

    [Fact]
    public void TheDocumentCarriesNoScriptAndNoBundleReference()
    {
        // The entire reason this is rendered at publish time: nothing here may
        // depend on a second request succeeding.
        var result = Renderer.Render(Request(Sentinels()));

        result.Value.Html.Should().NotContain("<script");
        result.Value.Html.Should().NotContain("/assets/");
        result.Value.Html.Should().Contain("<style>");
    }

    [Theory]
    [InlineData("ar", "rtl")]
    [InlineData("fa", "rtl")]
    [InlineData("ur", "rtl")]
    [InlineData("en", "ltr")]
    [InlineData("tr", "ltr")]
    public void TheDocumentDeclaresItsOwnLanguageAndDirection(string language, string direction)
    {
        var result = Renderer.Render(Request(Sentinels(), language: language));

        result.Value.Html.Should().Contain($"<html lang=\"{language}\" dir=\"{direction}\">");
    }

    [Fact]
    public void AFallbackLanguage_SaysSoInTheReadersOwnLanguage()
    {
        // Silent locale fallback is a deceptive pattern (EDPB Guidelines
        // 03/2022). A reader who cannot read English is exactly the reader who
        // cannot be told about the fallback in English.
        var result = Renderer.Render(
            Request(Sentinels(), language: "ar", isFallback: true));

        result.IsError.Should().BeFalse();
        result.Value.Html.Should().Contain("النسخة الإنجليزية");
    }

    [Fact]
    public void ATranslatedLanguage_CarriesNoFallbackNotice()
    {
        var result = Renderer.Render(Request(Sentinels(), language: "ar"));

        result.Value.Html.Should().NotContain("class=\"notice\"");
    }

    [Fact]
    public void AuthoredContentIsEscaped_SoADocumentCannotInjectMarkup()
    {
        var content = Sentinels();
        content.Intro = ["<img src=x onerror=alert(1)>"];

        var result = Renderer.Render(Request(content));

        result.Value.Html.Should().NotContain("<img src=x");
        result.Value.Html.Should().Contain("&lt;img src=x");
    }

    [Fact]
    public void LawReferencesLinkToTheirOfficialText()
    {
        var content = Sentinels();
        content.Intro = ["Your GDPR and KVKK rights."];

        var result = Renderer.Render(Request(content));

        result.Value.Html.Should().Contain("https://eur-lex.europa.eu/eli/reg/2016/679/oj");
        result.Value.Html.Should().Contain("mevzuat.gov.tr");
    }

    [Fact]
    public void TheCombinedLawTermIsMatchedBeforeItsParts()
    {
        // "CCPA/CPRA" must not match as "CCPA", which would leave a dangling
        // "/CPRA" outside the link.
        var content = Sentinels();
        content.Intro = ["Under CCPA/CPRA you may opt out."];

        var result = Renderer.Render(Request(content));

        result.Value.Html.Should().Contain(">CCPA/CPRA</a>");
    }

    [Fact]
    public void TheSameInputProducesTheSameHash()
    {
        // The hash is the ETag and the evidence of what a user was shown, so it
        // has to be a function of the content and nothing else.
        var first = Renderer.Render(Request(Sentinels()));
        var second = Renderer.Render(Request(Sentinels()));

        first.Value.ContentHash.Should().Be(second.Value.ContentHash);
        first.Value.ContentHash.Should().HaveLength(64);
    }

    [Fact]
    public void DifferentContentProducesADifferentHash()
    {
        var changed = Sentinels();
        changed.Title = "A revised title";

        var first = Renderer.Render(Request(Sentinels()));
        var second = Renderer.Render(Request(changed));

        first.Value.ContentHash.Should().NotBe(second.Value.ContentHash);
    }

    [Fact]
    public void BlankOptionalContacts_OmitTheirWholeLine()
    {
        // A label with nothing after it is worse than no line at all.
        var disclosure = Disclosure();
        disclosure.DpoContact = string.Empty;
        disclosure.VerbisNo = "12345";
        disclosure.KepAddress = string.Empty;

        var result = Renderer.Render(Request(Sentinels(), disclosure));

        result.Value.Html.Should().Contain("VERBIS-LABEL: 12345");
        result.Value.Html.Should().NotContain("DPO-LABEL");
        result.Value.Html.Should().NotContain("KEP-LABEL");
    }

    [Fact]
    public void TheDeletionEntryPointIsAnAbsoluteLinkIntoTheAccountsApp()
    {
        var result = Renderer.Render(Request(Sentinels()));

        result.Value.Html.Should().Contain(
            "href=\"https://accounts.example.com/delete-account\"");
    }

    [Fact]
    public void TheAuthoringOnlyDraftWarningIsNeverPublished()
    {
        // It describes an unsaved draft in the console editor. Publishing is
        // refused while the controller is incomplete, so on the public surface
        // the banner is unreachable rather than merely suppressed.
        var content = Sentinels();
        content.UnfilledWarning = "DRAFT — DO NOT PUBLISH";

        var result = Renderer.Render(Request(content));

        result.Value.Html.Should().NotContain("DRAFT — DO NOT PUBLISH");
    }

    private static readonly string[] SentinelValues =
    [
        "SENTINEL-TITLE", "SENTINEL-EFFECTIVE", "SENTINEL-VERSIONLABEL", "SENTINEL-INTRO",
        "SENTINEL-SECTION-HEADING", "SENTINEL-SECTION-PARAGRAPH", "SENTINEL-SECTION-BULLET",
        "SENTINEL-RETENTION-HEADING", "SENTINEL-RETENTION-INTRO", "SENTINEL-RETENTION-COLUMN",
        "SENTINEL-RETENTION-CATEGORY", "SENTINEL-RETENTION-PERIOD", "SENTINEL-RETENTION-DETAIL",
        "SENTINEL-DELETION-HEADING", "SENTINEL-DELETION-PARAGRAPH", "SENTINEL-DELETION-BULLET",
        "SENTINEL-DELETION-BUTTON", "SENTINEL-DELETION-HINT",
        "SENTINEL-RIGHTS-HEADING", "SENTINEL-CLOSING-HEADING"
    ];

    private static PolicyDocumentModel Sentinels() => new()
    {
        Title = "SENTINEL-TITLE",
        EffectiveDate = "SENTINEL-EFFECTIVE",
        VersionLabel = "SENTINEL-VERSIONLABEL",
        Intro = ["SENTINEL-INTRO"],
        Sections =
        [
            new PolicyDocumentSection
            {
                Heading = "SENTINEL-SECTION-HEADING",
                Paragraphs = ["SENTINEL-SECTION-PARAGRAPH"],
                Bullets = ["SENTINEL-SECTION-BULLET"]
            }
        ],
        Retention = new PolicyDocumentRetention
        {
            Heading = "SENTINEL-RETENTION-HEADING",
            Intro = "SENTINEL-RETENTION-INTRO",
            Columns = ["SENTINEL-RETENTION-COLUMN", "B", "C"],
            Rows =
            [
                new PolicyDocumentRetentionRow
                {
                    Category = "SENTINEL-RETENTION-CATEGORY",
                    Retention = "SENTINEL-RETENTION-PERIOD",
                    Detail = "SENTINEL-RETENTION-DETAIL"
                }
            ]
        },
        Deletion = new PolicyDocumentDeletion
        {
            Heading = "SENTINEL-DELETION-HEADING",
            Paragraphs = ["SENTINEL-DELETION-PARAGRAPH"],
            Bullets = ["SENTINEL-DELETION-BULLET"],
            Button = "SENTINEL-DELETION-BUTTON",
            SignedInHint = "SENTINEL-DELETION-HINT"
        },
        Rights = [new PolicyDocumentSection { Heading = "SENTINEL-RIGHTS-HEADING" }],
        Closing = [new PolicyDocumentSection { Heading = "SENTINEL-CLOSING-HEADING" }],
        ContactDpoLabel = "DPO-LABEL",
        ContactVerbisLabel = "VERBIS-LABEL",
        ContactKepLabel = "KEP-LABEL",
        UnfilledWarning = "authoring-only warning"
    };

    private static PrivacyPolicyDisclosureDto Disclosure() => new()
    {
        LegalName = "Acme Corp LLC",
        Address = "1 Example Street, Istanbul",
        PrivacyEmail = "privacy@example.com",
        EmailProvider = "Example Mail",
        HostingProvider = "Example Hosting",
        HostingCountry = "Türkiye",
        DpoContact = "dpo@example.com",
        VerbisNo = "12345",
        KepAddress = "acme@hs01.kep.tr",
        GraceDays = 30,
        OtpValidityMinutes = 15,
        LoginAttemptRetentionDays = 365,
        OutboxRetentionDays = 180,
        IdentifierReservationDays = 1095,
        PolicyVersion = "2026.07"
    };

    private static PolicyRenderRequest Request(
        PolicyDocumentModel content,
        PrivacyPolicyDisclosureDto? disclosure = null,
        string language = "en",
        bool isFallback = false) =>
        new(
            LanguageCode: language,
            Content: content,
            Disclosure: disclosure ?? Disclosure(),
            Version: "2026.07",
            AvailableLanguages: PolicyLanguages.Ordered,
            IsFallbackLanguage: isFallback,
            AccountsBaseUrl: "https://accounts.example.com");
}
