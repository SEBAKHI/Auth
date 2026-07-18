using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// Unit tests for the sandboxed Fluid renderer: encoding policy, syntax
/// validation, unresolved-variable tracking, and culture-aware filters.
/// </summary>
public class FluidTemplateRendererTests
{
    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void Render_SimpleVariables_SubstitutesValues()
    {
        var model = new Dictionary<string, object?> { ["UserName"] = "Jane", ["OtpCode"] = "123456" };

        var result = _renderer.Render("Hello {{ UserName }}, code {{ OtpCode }}", model, "en", encodeHtml: false);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("Hello Jane, code 123456");
    }

    [Fact]
    public void Render_HtmlContextWithHostileVariable_EncodesOutput()
    {
        var model = new Dictionary<string, object?> { ["UserName"] = "<script>alert(1)</script>" };

        var result = _renderer.Render("<p>{{ UserName }}</p>", model, "en", encodeHtml: true);

        result.IsError.Should().BeFalse();
        result.Value.Should().NotContain("<script>");
        result.Value.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Render_NonHtmlContext_EmitsRawValue()
    {
        var model = new Dictionary<string, object?> { ["UserName"] = "O'Brien & Sons" };

        var result = _renderer.Render("Hello {{ UserName }}", model, "en", encodeHtml: false);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("Hello O'Brien & Sons");
    }

    [Fact]
    public void Render_RawFilter_BypassesEncoding()
    {
        var model = new Dictionary<string, object?> { ["content"] = "<div>body</div>" };

        var result = _renderer.Render("{{ content | raw }}", model, "en", encodeHtml: true);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("<div>body</div>");
    }

    [Fact]
    public void Render_MissingVariable_RendersEmpty()
    {
        var result = _renderer.Render(
            "Hello {{ Missing }}!", new Dictionary<string, object?>(), "en", encodeHtml: true);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("Hello !");
    }

    [Fact]
    public void Render_ConditionalOnDir_EvaluatesBranch()
    {
        var model = new Dictionary<string, object?> { ["dir"] = "rtl" };

        var result = _renderer.Render(
            "align: {% if dir == \"rtl\" %}right{% else %}left{% endif %}", model, "ar", encodeHtml: true);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("align: right");
    }

    [Fact]
    public void Render_DateFilter_FormatsDateTime()
    {
        var model = new Dictionary<string, object?>
        {
            ["ExpiresAt"] = new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)
        };

        var result = _renderer.Render(
            "Expires {{ ExpiresAt | date: \"%Y-%m-%d %H:%M\" }} UTC", model, "en", encodeHtml: false);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("Expires 2026-12-31 23:59 UTC");
    }

    [Fact]
    public void Render_NestedDictionary_ResolvesMemberAccess()
    {
        var model = new Dictionary<string, object?>
        {
            ["strings"] = new Dictionary<string, object?> { ["footer"] = "Footer text" }
        };

        var result = _renderer.Render("{{ strings.footer | raw }}", model, "en", encodeHtml: true);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be("Footer text");
    }

    [Fact]
    public void Validate_InvalidSyntax_ReturnsSyntaxError()
    {
        var result = _renderer.Validate("Hello {% if %} broken");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.InvalidTemplateSyntax");
    }

    [Fact]
    public void Validate_ValidTemplate_Succeeds()
    {
        _renderer.Validate("Hello {{ UserName }}").IsError.Should().BeFalse();
    }

    [Fact]
    public void RenderTracking_UnknownVariables_AreReported()
    {
        var model = new Dictionary<string, object?> { ["Known"] = "x" };

        var result = _renderer.RenderTracking(
            "{{ Known }} {{ UnknownOne }} {{ UnknownTwo }}", model, "en", encodeHtml: false,
            out var unresolved);

        result.IsError.Should().BeFalse();
        unresolved.Should().BeEquivalentTo(["UnknownOne", "UnknownTwo"]);
    }

    [Fact]
    public void RenderTracking_AllVariablesSupplied_ReportsNone()
    {
        var model = new Dictionary<string, object?> { ["UserName"] = "Jane", ["OtpCode"] = "1" };

        _renderer.RenderTracking(
            "{{ UserName }}: {{ OtpCode }}", model, "en", encodeHtml: false, out var unresolved);

        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void Render_UnboundedLoop_IsStoppedByStepLimit()
    {
        // 100k iterations exceeds the MaxSteps sandbox bound.
        var source = "{% for i in (1..100000) %}x{% endfor %}";

        var result = _renderer.Render(source, new Dictionary<string, object?>(), "en", encodeHtml: false);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.RenderFailed");
    }
}
