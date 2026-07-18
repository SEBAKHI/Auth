using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// Unit tests for the plain-text derivation used when a translation has no
/// explicit BodyText template.
/// </summary>
public class HtmlToTextConverterTests
{
    [Fact]
    public void Convert_ParagraphsAndBreaks_BecomeNewlines()
    {
        var text = HtmlToTextConverter.Convert("<p>First</p><p>Second<br>Third</p>");

        text.Should().Be("First\n\nSecond\nThird");
    }

    [Fact]
    public void Convert_AnchorWithDifferentText_BecomesTextWithUrl()
    {
        var text = HtmlToTextConverter.Convert(
            "<a class=\"button\" href=\"https://example.com/reset\">Reset Password</a>");

        text.Should().Be("Reset Password (https://example.com/reset)");
    }

    [Fact]
    public void Convert_AnchorWhoseTextIsTheUrl_EmitsUrlOnce()
    {
        var text = HtmlToTextConverter.Convert(
            "<a href=\"https://example.com/reset\">https://example.com/reset</a>");

        text.Should().Be("https://example.com/reset");
    }

    [Fact]
    public void Convert_Entities_AreDecoded()
    {
        var text = HtmlToTextConverter.Convert("<p>Tom &amp; Jerry &lt;3</p>");

        text.Should().Be("Tom & Jerry <3");
    }

    [Fact]
    public void Convert_StyleBlock_IsRemovedEntirely()
    {
        var text = HtmlToTextConverter.Convert(
            "<style>body { color: red; }</style><p>Visible</p>");

        text.Should().Be("Visible");
    }

    [Fact]
    public void Convert_FullEmailFragment_ReadsSensibly()
    {
        var html = """
            <div class="header"><h1>Password Reset</h1></div>
            <p class="message">Hello Jane,</p>
            <div class="button-container"><a class="button" href="https://x.test/r?t=1">Reset</a></div>
            <div class="warning">Security Notice: ignore if unexpected.</div>
            """;

        var text = HtmlToTextConverter.Convert(html);

        text.Should().Contain("Password Reset");
        text.Should().Contain("Hello Jane,");
        text.Should().Contain("Reset (https://x.test/r?t=1)");
        text.Should().Contain("Security Notice: ignore if unexpected.");
        text.Should().NotContain("<");
    }
}
