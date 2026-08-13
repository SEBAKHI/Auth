using System.Text;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the email layout's deployment contract and the dark-mode/logo techniques that two
/// real-client defects were traced to.
///
/// Every assertion here exists because the corresponding mistake already shipped once: a
/// transparent frame that partial-inverting clients could not convert, an Outlook block whose
/// selectors could never match, and a logo pointed at an alpha-carrying WebP that Gmail
/// transcodes to JPEG and flattens onto black. None of these are visible to curl or to a unit
/// test of the rendered payload - only to a real mail client - so the guard has to be on the
/// source that produces them.
/// </summary>
public class EmailLayoutContractTests
{
    // The layout is carried by a CHAIN of upgrade scripts, each fingerprinting the previous
    // one's output. Only the newest still matches the seed; older ones are frozen historical
    // steps whose fingerprints are already spent on every database that ran them.
    private const string UpgradeScript = "2026-08-10_EmailLayoutRtlHardening.sql";

    private static readonly string[] UpgradeChain =
    [
        "2026-07-31_EmailLayoutLogoPlatformDriven.sql",
        "2026-08-10_EmailLayoutDarkModeAndLogo.sql",
        "2026-08-10_EmailLayoutRtlHardening.sql"
    ];

    private const string SeedScript = "11_NotificationLayouts.sql";
    private const string LayoutDeclaration = "DECLARE @LayoutContent NVARCHAR(MAX) = N'";

    [Fact]
    public void SeedAndUpgrade_CarryByteIdenticalLayout()
    {
        // A fresh database is seeded from 11_; an existing one is rewritten by the upgrade.
        // If the two literals drift, the bug is fixed on dev and alive in production - the
        // classic "works on my machine" failure, and one a code review will not catch.
        ExtractLayout(File.ReadAllText(SeedPath(SeedScript)))
            .Should().Be(ExtractLayout(File.ReadAllText(UpgradePath(UpgradeScript))),
                "fresh and upgraded databases must end up with the same email layout");
    }

    [Fact]
    public void UpgradeChain_RunsInOrderInPostDeployment()
    {
        // Each script fingerprints the previous one's OUTPUT, so the order is load-bearing:
        // run them out of sequence and every UPDATE silently matches zero rows.
        var postDeployment = File.ReadAllText(Path.Combine(
            DbScriptsDirectory(), "..", "PostDeployment", "Script.PostDeployment.sql"));

        var previousIndex = -1;
        foreach (var script in UpgradeChain)
        {
            var index = postDeployment.IndexOf(script, StringComparison.OrdinalIgnoreCase);
            index.Should().BeGreaterThan(-1,
                $"'{script}' only runs because of its :r include - a <None> entry in the " +
                "sqlproj is project visibility, not execution");
            index.Should().BeGreaterThan(previousIndex,
                $"'{script}' consumes the output of the script before it in the chain");
            previousIndex = index;
        }
    }

    [Fact]
    public void SupersededUpgradeScripts_AreMarkedFrozen()
    {
        // Editing a spent literal in place applies to fresh developer databases and to no
        // production database at all - a no-op that reads like a fix in review.
        var superseded = File.ReadAllText(UpgradePath("2026-08-10_EmailLayoutDarkModeAndLogo.sql"));

        superseded.Should().Contain("FROZEN",
            "a superseded layout script must say so, or the next person edits its dead literal");
    }

    [Fact]
    public void UpgradeScript_IsSqlcmdSafe()
    {
        var bytes = File.ReadAllBytes(UpgradePath(UpgradeScript));

        (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse(
            ":r inlines the file into an already-open batch, so a BOM would land mid-stream");
        bytes.Should().OnlyContain(b => b < 0x80,
            "a BOM-free include must stay ASCII or sqlcmd mangles it");

        var script = File.ReadAllText(UpgradePath(UpgradeScript));
        script.Should().Contain("SET QUOTED_IDENTIFIER ON",
            "sqlcmd rejects DML against filtered indexes without it (Msg 1934)");
        script.TrimEnd().Should().EndWith("GO",
            ":r-included files are inlined into the post-deploy batch and must terminate it");
    }

    [Fact]
    public void SeedScript_KeepsItsBomForNonAsciiChromeStrings()
    {
        var bytes = File.ReadAllBytes(SeedPath(SeedScript));

        (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeTrue(
            "the per-language footer strings are Arabic/Chinese/Urdu/Persian and sqlcmd mangles " +
            "them without a BOM");
        File.ReadAllText(SeedPath(SeedScript)).Should().Contain("هذه رسالة تلقائية",
            "the Arabic chrome string must survive whatever re-encoded the file last");
    }

    [Fact]
    public void Layout_ActivatesDarkModeTheWayAppleMailRequires()
    {
        var layout = SeedLayout();

        layout.Should().Contain(":root { color-scheme: light dark; }",
            "Apple Mail 13+ ignores the meta notation and applies the dark block only when the " +
            "CSS property is present");
        layout.Should().Contain("<meta name=\"supported-color-schemes\"",
            "the meta form is still the only activation Apple Mail 12 understands");
        layout.Should().NotContain("supported-color-schemes:",
            "there is no such CSS property; a strict sanitiser can drop the whole :root rule on " +
            "the parse error, taking Apple Mail dark mode with it");
    }

    [Fact]
    public void Layout_UsesOnlyOutlookSelectorFormsOutlookSupports()
    {
        // Comments are stripped first: the block documents `.card[data-ogsb]` as the form to
        // avoid, and matching that would fail the test for saying the right thing.
        var layout = WithoutCssComments(SeedLayout());

        // Outlook.com supports [attr] and E[attr], not .class[attr]. An unsupported selector
        // inside a comma-separated group invalidates the ENTIRE group, so one of these would
        // silently take the working descendant rule down with it.
        foreach (var attribute in new[] { "data-ogsb", "data-ogsc", "data-ogab", "data-ogac" })
        {
            System.Text.RegularExpressions.Regex
                .IsMatch(layout, $@"\.[A-Za-z0-9_-]+\[{attribute}\]")
                .Should().BeFalse(
                    $"a class+attribute selector on {attribute} is unsupported and invalidates its whole rule group");
        }

        layout.Should().Contain("[data-ogsb] .card",
            "the descendant form is the one Outlook actually matches");
    }

    [Fact]
    public void Layout_HasAnOpaqueFrameSoInvertingClientsHaveAColourToConvert()
    {
        var layout = SeedLayout();

        layout.Should().NotContain("background:transparent",
            "a transparent surface gives a partial-inverting client nothing to convert, so it " +
            "drops the message onto its own background while the card stays light - and it " +
            "leaves Outlook no recoloured ancestor for the [data-ogsb] rules to hang off");
        layout.Should().Contain(".wrapper { background-color:#F1F1EF;",
            "the frame must declare an explicit colour");
        layout.Should().Contain("<body bgcolor=\"#F1F1EF\"",
            "the attribute is the belt to the CSS braces for clients that strip <style>");
    }

    [Fact]
    public void Layout_PointsAtEmailRenditionsNeverTheRawUpload()
    {
        var layout = SeedLayout();

        layout.Should().Contain("{{ Platform.EmailLogoUrl }}");
        layout.Should().Contain("{{ Platform.EmailLogoDarkUrl }}");
        layout.Should().NotContain("{{ Platform.LogoUrl }}",
            "the raw upload is an alpha-carrying WebP: Gmail transcodes it to JPEG and flattens " +
            "the transparency onto black, and Outlook for Windows cannot decode WebP at all");
    }

    [Fact]
    public void Layout_HidesTheDarkLogoFromOutlookWithAConditionalComment()
    {
        var layout = SeedLayout();

        layout.Should().Contain("<!--[if !mso]><!-->",
            "Outlook's Word engine does not honour display:none on an <img>, so the second logo " +
            "must be hidden from its parser entirely or both render");
        layout.Should().Contain("<!--<![endif]-->");
        layout.Should().Contain("style=\"display:none;\"",
            "the inline base state is what keeps the dark logo hidden in clients that strip <style>");
    }

    [Fact]
    public void Layout_SwapsTheLogoOnlyWhenADarkOneExists()
    {
        var layout = SeedLayout();

        layout.Should().Contain("{% if Platform.EmailLogoDarkUrl %} has-dark{% endif %}",
            "without the gate an unset dark logo would hide the light chip and show nothing");
        layout.Should().Contain(".logo.has-dark .logo-light { display:none !important; }");
        layout.Should().Contain(".logo.has-dark .logo-dark { display:block !important; }");
    }

    [Fact]
    public void Layout_GivesEveryColourBearingRuleADarkOverride()
    {
        var layout = SeedLayout();
        var darkBlock = Between(layout, "@media (prefers-color-scheme: dark) {", "\n}");

        // A partial dark block is worse than none: it darkens some surfaces and leaves others
        // light, which is exactly what the reported bug looked like.
        foreach (var selector in new[]
        {
            // .wrapper-cell is listed because it carries an INLINE background: an inline
            // declaration beats an unqualified rule, so it silently kept the frame light
            // around a dark card until it got its own override.
            "html, body", "body", ".wrapper", ".wrapper-cell", ".card", ".top-accent", ".application",
            ".brand-rule div", ".header h1", ".eyebrow", ".message", ".muted", "strong",
            ".button", ".otp-code", ".link-box", ".notice", ".notice-title", ".notice-text",
            ".footer", ".footer p", ".subfooter", ".subfooter p", ".content"
        })
        {
            darkBlock.Should().Contain(selector,
                $"'{selector}' carries a colour in light mode and needs a dark counterpart");
        }
    }

    [Fact]
    public void EveryPaintedElementInTheMarkupCanBeDarkened()
    {
        // The general form of a bug that shipped: an element painted with a bgcolor attribute
        // or an inline background-color, but with no class the dark block can target. An
        // inline declaration outranks an unqualified rule, so such an element stays light on a
        // dark card - and it is invisible to a light-mode-only review.
        var layout = WithoutCssComments(SeedLayout());
        var markup = layout[layout.IndexOf("<body", StringComparison.Ordinal)..];
        var darkBlock = Between(layout, "@media (prefers-color-scheme: dark) {", "\n}");

        var painted = System.Text.RegularExpressions.Regex.Matches(
            markup, @"<(?<tag>body|table|td)\b(?<attrs>[^>]*)>");

        foreach (System.Text.RegularExpressions.Match tag in painted)
        {
            var attrs = tag.Groups["attrs"].Value;
            if (!attrs.Contains("bgcolor=", StringComparison.OrdinalIgnoreCase) &&
                !attrs.Contains("background-color:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tag.Groups["tag"].Value == "body")
            {
                darkBlock.Should().Contain("body",
                    "the body element is painted and must be darkenable");
                continue;
            }

            var className = System.Text.RegularExpressions.Regex
                .Match(attrs, @"class=""(?<c>[^""{]*)").Groups["c"].Value.Trim();

            className.Should().NotBeEmpty(
                $"the painted element <{tag.Groups["tag"].Value}{attrs}> has no class, so no dark rule can reach it");

            var firstClass = className.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            darkBlock.Should().Contain($".{firstClass}",
                $"'.{firstClass}' paints a surface in light mode and needs a dark override");
        }
    }

    [Fact]
    public void BaseDirection_SurvivesWithoutTheHtmlTagOrTheStyleBlock()
    {
        // Gmail strips <html>/<head> and replaces <body> with a <div> before grafting the
        // message into its own LTR page, so `<html dir>` never exists there and a
        // `body { direction }` rule matches nothing. Direction must therefore be carried by
        // elements INSIDE <body>. This test simulates that amputation.
        var layout = SeedLayout();
        var markup = layout[layout.IndexOf("<body", StringComparison.Ordinal)..];
        var afterBodyTag = markup[(markup.IndexOf('>') + 1)..];

        afterBodyTag.Should().Contain("dir=\"{{ dir }}\"",
            "with <html> and <body> gone, direction only survives on elements inside the body");

        // Every text-bearing container must carry it, not just the outermost one: some
        // clients strip dir from <table>/<td> specifically.
        System.Text.RegularExpressions.Regex.Matches(afterBodyTag, @"dir=""\{\{ dir \}\}""")
            .Count.Should().BeGreaterThanOrEqualTo(6,
                "direction is deliberately redundant - you cannot predict where a sanitiser cuts");

        // A non-table carrier is required: the content wrapper div and the footer paragraphs.
        afterBodyTag.Should().Contain("<div dir=\"{{ dir }}\"",
            "clients that strip dir from table/td leave the content unanchored without a div carrier");
        afterBodyTag.Should().Contain("<p dir=\"{{ dir }}\"",
            "the footer paragraphs need their own carrier for the same reason");

        // Inline direction survives even total <style> loss.
        afterBodyTag.Should().Contain("direction:{{ dir }};",
            "an inline declaration is the belt to the dir attribute's braces");
    }

    [Fact]
    public void Layout_NeverUsesFlowRelativeTextAlign()
    {
        // start/end are unsupported in every Outlook for Windows, Yahoo, AOL, Orange, GMX
        // and Web.de. Direction-aware alignment must be written through the {{ dir }}
        // conditional as an absolute keyword.
        var layout = WithoutCssComments(SeedLayout());

        layout.Should().NotContain("text-align:start",
            "flow-relative alignment silently falls back to left in a large share of clients");
        layout.Should().NotContain("text-align:end");
        layout.Should().NotContain("text-align: start");
    }

    [Fact]
    public void InterpolatedIdentityValues_AreBidiIsolated()
    {
        // A tenant name ending in a neutral ("Company Inc.") merges its own period with the
        // sentence's, and the pair gets ejected from the Latin run - rendering ".Company Inc".
        // dir="auto" gives the span first-strong detection plus UA unicode-bidi:isolate.
        var markup = SeedLayout();

        markup.Should().Contain("<span dir=\"auto\">{{ Application.Name }}</span>");
        markup.Should().Contain("<span dir=\"auto\">{{ Platform.Name }}</span>");
    }

    [Fact]
    public void Layout_StyleBlockStaysUnderGmailsCap()
    {
        var style = Between(SeedLayout(), "<style>", "</style>");
        var bytes = Encoding.UTF8.GetByteCount(style);

        // Gmail desktop webmail silently drops a <style> tag over 16 KB - and Gmail desktop is
        // the one Gmail surface that renders the light design correctly today, so overrunning
        // would break the client that currently works.
        bytes.Should().BeLessThan(12 * 1024,
            $"the <style> block is {bytes} bytes; keep a margin under Gmail's 16 KB cap");
    }

    private static string SeedLayout() => ExtractLayout(File.ReadAllText(SeedPath(SeedScript)));

    private static string WithoutCssComments(string layout) =>
        System.Text.RegularExpressions.Regex.Replace(
            layout, @"/\*[\s\S]*?\*/", string.Empty);

    private static string ExtractLayout(string script)
    {
        var start = script.IndexOf(LayoutDeclaration, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the script must declare @LayoutContent");
        start += LayoutDeclaration.Length;

        var end = script.IndexOf("</html>';", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the @LayoutContent literal must be terminated");
        return script[start..end];
    }

    private static string Between(string source, string open, string close)
    {
        var start = source.IndexOf(open, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{open}' must exist in the layout");
        start += open.Length;

        var end = source.IndexOf(close, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"'{close}' must close '{open}'");
        return source[start..end];
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(DbScriptsDirectory(), "SeedData", fileName);

    private static string UpgradePath(string fileName) =>
        Path.Combine(DbScriptsDirectory(), "Upgrades", fileName);

    private static string DbScriptsDirectory() =>
        Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Scripts");

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
