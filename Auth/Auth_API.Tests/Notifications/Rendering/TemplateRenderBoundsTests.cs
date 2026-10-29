using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Infrastructure.Notifications;
using Auth.Infrastructure.Notifications.Liquid;
using ErrorOr;
using Fluid;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// Every Liquid render is bounded while it runs: a short template must not make the server
/// allocate, recurse or loop in proportion to what it would produce. The worst cases below were
/// measured against Fluid 2.40.0 unbounded on 2026-10-29: the loop allocated 577 MB, the doubling
/// had no bound at all, a wide range ran the process out of memory, and the nesting cases ended
/// the process with a stack overflow.
/// </summary>
public class TemplateRenderBoundsTests
{
    /// <summary>
    /// What one refused render may allocate: a small multiple of the 100 KB cap, whatever the
    /// template asked for (every case below asked for hundreds of megabytes or more).
    /// </summary>
    private const long AllocationCeiling = 8 * 1024 * 1024;

    private static readonly Dictionary<string, object?> NoModel = [];

    private readonly FluidTemplateRenderer _renderer = new(TimeProvider.System);

    // --- the measured worst cases ---

    [Fact]
    public void Render_LoopOverALargeString_StopsAtTheCapWithoutBuildingTheOutput()
    {
        var (result, allocated, elapsed) = Measure(
            "{% for i in (1..2400) %}{{ strings.big | raw }}{% endfor %}", BigStrings());

        ShouldBeOutputTooLarge(result, NotificationLimits.MaxRenderedBytes);
        allocated.Should().BeLessThan(AllocationCeiling, "it allocated 577 MB unbounded");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(40)]
    public void Render_AssignThatDoublesAString_IsRefusedWithoutBuildingIt(int doublings)
    {
        var (result, allocated, _) = Measure(
            "{% assign x = \"ab\" %}{% for i in (1.." + doublings + ") %}{% assign x = x | append: x %}{% endfor %}{{ x | size }}",
            NoModel);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Theory]
    [InlineData("{% for i in (1..5000000) %}{% break %}{% endfor %}")]
    [InlineData("{% assign r = (1..2000000000) %}{{ r | size }}")]
    [InlineData("{% assign x = \"abc\" %}{{ x[(1..2000000000)] }}")]
    [InlineData("{% for i in (-2147483648..2147483647) %}{% break %}{% endfor %}")]
    public void Render_WideRange_IsRefusedBeforeTheArrayIsBuilt(string source)
    {
        var (result, allocated, _) = Measure(source, NoModel);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Theory]
    [InlineData("{% capture c %}{% for i in (1..2400) %}{{ strings.big | raw }}{% endfor %}{% endcapture %}{{ c | size }}")]
    [InlineData("{% for i in (1..2400) %}{% ifchanged %}{{ strings.big | raw }}{{ i }}{% endifchanged %}{% endfor %}")]
    public void Render_BufferedBlockOfALoop_IsStoppedWhileItBuffers(string source)
    {
        var (result, allocated, _) = Measure(source, BigStrings());

        result.IsError.Should().BeTrue();
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    [Theory]
    [InlineData("{% capture c %}{{ strings.big | raw }}{{ strings.big | raw }}{% endcapture %}{{ c | size }}")]
    [InlineData("{% ifchanged %}{{ strings.big | raw }}{{ strings.big | raw }}{% endifchanged %}")]
    public void Render_BufferedBlockLongerThanAValue_IsRefusedAtTheValueCap(string source)
    {
        // 120,000 characters: within the session's work, but longer than any one value may be.
        var (result, _, _) = Measure(source, BigStrings());

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
    }

    [Theory]
    [InlineData("{{ \"now\" | date: \"%100000000n\" | size }}")]
    [InlineData("{{ \"now\" | date: \"%-_100000000N\" | size }}")]
    [InlineData("{{ 1 | format_number: \"F100000000\" | size }}")]
    [InlineData("{{ \"{0,999999}{0,999999}\" | format_string: 1 | size }}")]
    [InlineData("{{ \"{0:F100000000}\" | format_string: 1 | size }}")]
    [InlineData("{{ s | replace: \"a\", s | size }}")]
    [InlineData("{{ s | split: \"\" | join: s | size }}")]
    public void Render_FilterThatBuildsFromItsArgument_IsRefusedBeforeItRuns(string source)
    {
        var model = new Dictionary<string, object?> { ["s"] = new string('a', 10_000) };

        var (result, allocated, _) = Measure(source, model);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling, "each one allocated 200 to 735 MB unbounded");
    }

    [Theory]
    [InlineData("{% assign a = s | split: \",\" %}{% for i in (1..30) %}{% assign a = a | concat: a %}{% endfor %}{{ a | size }}")]
    [InlineData("{% for i in (1..4000) %}{% assign x = s | upcase %}{% endfor %}{{ x | size }}")]
    public void Render_WorkAcrossManyStatements_IsBoundedByTheSessionBudget(string source)
    {
        var model = new Dictionary<string, object?> { ["s"] = new string('a', 60_000) };

        var (result, allocated, _) = Measure(source, model);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling);
    }

    // --- nesting: these ended the test host before the fix ---

    [Theory]
    [InlineData("and", 10_000)]
    [InlineData("filters", 13_000)]
    [InlineData("ifs", 6_000)]
    [InlineData("ifs", 45_000)]
    [InlineData("fors", 6_000)]
    [InlineData("captures", 6_000)]
    [InlineData("indexers", 30_000)]
    [InlineData("elsifs", 3_000)]
    public void Render_DeeplyNestedTemplate_IsRefusedInsteadOfOverflowingTheStack(string construct, int depth)
    {
        var source = Nested(construct, depth);

        var rendered = OnSmallStack(() => Session().Render(source, XModel(), "en", encodeHtml: true));
        var validated = OnSmallStack(() => _renderer.Validate(source));

        rendered.FirstError.Should().Be(TemplateErrors.TooComplex);
        validated.FirstError.Should().Be(TemplateErrors.TooComplex, "the save gates parse before they store");
    }

    [Theory]
    [InlineData("and", 31)]
    [InlineData("filters", 31)]
    [InlineData("ifs", 24)]
    [InlineData("fors", 24)]
    [InlineData("captures", 24)]
    [InlineData("indexers", 22)]
    [InlineData("elsifs", 24)]
    public void Render_DeepestAllowedNesting_RendersOnAStackLeftAfterTheHostsOwnFrames(string construct, int depth)
    {
        var deepest = OnIisStack(() => Session().Render(Nested(construct, depth), XModel(), "en", encodeHtml: true));
        var deeper = OnIisStack(() => Session().Render(Nested(construct, depth + 1), XModel(), "en", encodeHtml: true));

        deepest.IsError.Should().BeFalse(deepest.IsError ? deepest.FirstError.Description : string.Empty);
        deeper.FirstError.Should().Be(TemplateErrors.TooComplex, "one level deeper is past the limit");
    }

    [Fact]
    public void Render_DeepestAllowedOfSeveralKindsTogether_RendersOnASmallStack()
    {
        var innermost = "{{ x" + string.Concat(Enumerable.Repeat(" | raw", 31)) + " }}";
        var source = string.Concat(Enumerable.Repeat("{% for i in (1..1) %}{% capture c %}", 10))
            + "{% if x %}" + innermost + "{% endif %}"
            + string.Concat(Enumerable.Repeat("{% endcapture %}{{ c }}{% endfor %}", 10));

        var result = OnIisStack(() => Session().Render(source, XModel(), "en", encodeHtml: true));

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Description : string.Empty);
        result.Value.Should().Be("1");
    }

    // --- time ---

    [Fact]
    public void Render_LoopOverExpensiveConditions_IsStoppedByTheRenderTime()
    {
        // Every candidate fails at its last character, so each contains is quadratic (about 10 ms
        // here); 51 of them in each of 4,999 iterations is close to an hour of CPU unbounded.
        var model = new Dictionary<string, object?>
        {
            ["h"] = new StringBuilder().Insert(0, "ab", 25_000).ToString(),
            ["n"] = new StringBuilder().Insert(0, "ab", 12_499).Append("aa").ToString(),
        };
        var conditions = string.Concat(Enumerable.Repeat("{% elsif h contains n %}", 50));
        var source = "{% for i in (1..4999) %}{% if h contains n %}" + conditions + "{% endif %}{% endfor %}";

        var (result, _, elapsed) = Measure(source, model);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "the render time is one second, plus one operation");
    }

    [Fact]
    public void Render_OnceTheRenderTimeIsSpent_IsRefused()
    {
        var clock = new SteppingTimeProvider { Step = TimeSpan.FromMilliseconds(300) };

        var result = new FluidTemplateRenderer(clock).BeginSession(TemplateRenderLimits.Notification)
            .Render("{% for i in (1..10) %}{{ i }}{% endfor %}", NoModel, "en", encodeHtml: false);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
    }

    [Fact]
    public void Render_TimeBetweenTheRendersOfASession_IsNotCounted()
    {
        var clock = new SteppingTimeProvider();
        var session = new FluidTemplateRenderer(clock).BeginSession(TemplateRenderLimits.Notification);

        var first = session.Render("{{ 1 }}", NoModel, "en", encodeHtml: false);
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = session.Render("{{ 2 }}", NoModel, "en", encodeHtml: false);

        first.IsError.Should().BeFalse();
        second.IsError.Should().BeFalse("five minutes of I/O between two parts is not render time");
        second.Value.Should().Be("2");
    }

    // --- the unit the cap counts ---

    [Theory]
    [InlineData('x', 1)]
    [InlineData('ب', 2)]
    [InlineData('中', 3)]
    public void Render_OutputExactlyAtTheCapInUtf8Bytes_IsAllowed_AndOneByteMoreIsNot(char character, int bytesPerChar)
    {
        var limits = new TemplateRenderLimits(30_000);
        var atCap = new string(character, 30_000 / bytesPerChar);
        var model = new Dictionary<string, object?> { ["v"] = atCap };

        var allowed = _renderer.BeginSession(limits).Render("{{ v }}", model, "en", encodeHtml: false);
        var refused = _renderer.BeginSession(limits).Render("{{ v }}x", model, "en", encodeHtml: false);

        allowed.IsError.Should().BeFalse();
        Encoding.UTF8.GetByteCount(allowed.Value).Should().Be(30_000);
        ShouldBeOutputTooLarge(refused, 30_000);
    }

    [Fact]
    public void Render_SurrogatePairs_CountFourBytesEach()
    {
        var limits = new TemplateRenderLimits(40_000);
        var model = new Dictionary<string, object?> { ["v"] = string.Concat(Enumerable.Repeat("😀", 10_000)) };

        var allowed = _renderer.BeginSession(limits).Render("{{ v }}", model, "en", encodeHtml: false);
        var refused = _renderer.BeginSession(limits).Render("{{ v }}x", model, "en", encodeHtml: false);

        allowed.IsError.Should().BeFalse();
        ShouldBeOutputTooLarge(refused, 40_000);
    }

    [Fact]
    public void Render_EncodedOutput_IsCountedAfterEncoding()
    {
        var model = new Dictionary<string, object?> { ["v"] = new string('<', 30_000) };

        var raw = Session().Render("{{ v }}", model, "en", encodeHtml: false);
        var encoded = Session().Render("{{ v }}", model, "en", encodeHtml: true);

        raw.IsError.Should().BeFalse("30,000 characters fit in 100,000 bytes");
        ShouldBeOutputTooLarge(encoded, NotificationLimits.MaxRenderedBytes, "encoded, each < is &lt;: 120,000 bytes");
    }

    [Fact]
    public void Render_ModelValueLongerThanTheCap_IsRefusedBeforeItIsEncodedOrSearched()
    {
        var model = new Dictionary<string, object?>
        {
            ["sample"] = new Dictionary<string, object?> { ["big"] = new string('<', 5_000_000) }
        };

        var (result, allocated, _) = Measure("{{ sample.big }}", model);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        allocated.Should().BeLessThan(AllocationCeiling, "encoding it would have built 20 MB first");
    }

    // --- sessions and the cache ---

    [Fact]
    public void Session_PartsShareOneWorkBudget()
    {
        var model = new Dictionary<string, object?> { ["v"] = new string('x', 90_000) };
        var session = Session();

        var results = Enumerable.Range(0, 10)
            .Select(_ => session.Render("{{ v }}", model, "en", encodeHtml: false))
            .ToList();

        results.Take(8).Should().OnlyContain(r => !r.IsError, "eight 90,000-character parts fit 800,000");
        results[9].FirstError.Should().Be(
            TemplateErrors.TooComplex, "a template cannot multiply its budget by splitting into parts");
    }

    [Fact]
    public void Render_TheCachedTemplate_ObeysTheLimitsOfEachSession()
    {
        const string source = "{% for i in (1..150) %}0123456789{% endfor %}";

        var small = _renderer.BeginSession(new TemplateRenderLimits(1_000)).Render(source, NoModel, "en", encodeHtml: false);
        var large = _renderer.BeginSession(TemplateRenderLimits.Notification).Render(source, NoModel, "en", encodeHtml: false);
        var smallAgain = _renderer.BeginSession(new TemplateRenderLimits(1_000)).Render(source, NoModel, "en", encodeHtml: false);

        ShouldBeOutputTooLarge(small, 1_000);
        large.Value.Should().HaveLength(1_500);
        ShouldBeOutputTooLarge(smallAgain, 1_000);
    }

    [Fact]
    public void Parse_CacheHoldsABoundedAmountOfSource()
    {
        for (var i = 0; i < 40; i++)
        {
            var source = new string('x', 500_000) + i;

            _renderer.Validate(source).IsError.Should().BeFalse();
            _renderer.CachedSourceCharacters.Should().BeLessThanOrEqualTo(8 * 1024 * 1024 + source.Length);
        }

        _renderer.CachedTemplateCount.Should().BeLessThan(40, "40 sources of 500,000 characters are 20 million");
    }

    // --- legitimate content renders as Fluid renders it ---

    [Theory]
    [InlineData("{% for i in items %}{{ forloop.index }}:{{ i }}{% unless forloop.last %},{% endunless %}{% endfor %}")]
    [InlineData("{% for i in (1..10) %}{% if i == 3 %}{% continue %}{% endif %}{% if i > 6 %}{% break %}{% endif %}{{ i }}{% endfor %}")]
    [InlineData("{% for i in items limit: 2 %}{{ i }}{% endfor %}|{% for i in items offset: continue %}{{ i }}{% endfor %}")]
    [InlineData("{% for i in items reversed %}{{ i }}{% else %}none{% endfor %}{% for i in nothing %}x{% else %}none{% endfor %}")]
    [InlineData("{% cycle 'a', 'b' %}{% cycle 'a', 'b' %}{% cycle 'a', 'b' %}")]
    [InlineData("{% tablerow i in items cols: 2 %}{{ i }}{% endtablerow %}")]
    [InlineData("{% case n %}{% when 1 %}one{% when 2, 3 %}two-or-three{% else %}many{% endcase %}")]
    [InlineData("{% capture greeting %}Hello {{ user.Name }}{% endcapture %}{{ greeting | upcase }}|{{ greeting }}")]
    [InlineData("{% for i in items %}{% ifchanged %}{{ i | size }}{% endifchanged %}{% endfor %}")]
    [InlineData("{% assign x = 'a' %}{% echo x %}{%- echo 'b' -%} c")]
    [InlineData("{% raw %}{{ not }}{% endraw %}{% comment %}hidden{% endcomment %}{% increment c %}{% increment c %}{% decrement d %}")]
    [InlineData("  {%- if n == 3 -%}  trimmed  {%- endif -%}  ")]
    [InlineData("{{ \"2026-10-29\" | date: \"%A %d %B %Y\" }}|{{ items | join: \"-\" }}|{{ \"a,b\" | split: \",\" | size }}|{{ \"hello\" | replace: \"l\", \"L\" }}|{{ missing | default: \"fallback\" }}")]
    [InlineData("{{ html }}|{{ html | raw }}|{{ html | escape }}|{{ user.Name }}|{{ user.Name | url_encode }}")]
    [InlineData("{% if items contains \"b\" %}yes{% endif %}{% if \"abc\" contains \"bc\" %}yes{% endif %}{% if user.Name contains \"Jane\" and n > 2 or false %}yes{% endif %}")]
    [InlineData("{% for i in (1..n) %}{{ items[forloop.index0] }}{% endfor %}{{ items | first }}{{ items.last }}{{ items.size }}{{ user[\"Name\"] }}")]
    [InlineData("{% for a in items %}{% for b in items %}{{ forloop.index }}{% endfor %};{% endfor %}")]
    [InlineData("{% unless n == 1 %}a{% else %}b{% endunless %}{% if n == 1 %}1{% elsif n == 2 %}2{% elsif n == 3 %}3{% else %}e{% endif %}")]
    [InlineData("{{ 3.14159 | round: 2 }}|{{ 10 | divided_by: 3 }}|{{ \"%\" | append: n }}|{{ 1234.5 | format_number: \"N1\" }}|{{ \"{0}-{1}\" | format_string: \"a\", n }}")]
    [InlineData("{{ items | reverse | join: \",\" }}|{{ items | sort | first }}|{{ \"a b c\" | truncatewords: 2 }}|{{ \"line\nbreak\" | newline_to_br }}")]
    public void Render_LegitimateTemplate_MatchesUnboundedFluid(string source)
    {
        var model = new Dictionary<string, object?>
        {
            ["items"] = new List<object?> { "a", "b", "cc" },
            ["nothing"] = new List<object?>(),
            ["n"] = 3,
            ["user"] = new Dictionary<string, object?> { ["Name"] = "Jane & Co" },
            ["html"] = "<b>x</b>",
        };

        var bounded = Session().Render(source, model, "en", encodeHtml: true);

        bounded.IsError.Should().BeFalse(bounded.IsError ? bounded.FirstError.Description : string.Empty);
        bounded.Value.Should().Be(RenderWithUnboundedFluid(source, model));
    }

    [Fact]
    public void Filters_EveryFilterFluidRegisters_HasBeenReviewedForTheLengthOfItsResult()
    {
        var registered = new TemplateOptions().Filters.Select(filter => filter.Key).ToHashSet(StringComparer.Ordinal);

        registered.Should().BeEquivalentTo(BoundedFilters.ReviewedFilters,
            "a filter added by a Fluid upgrade is unbounded until someone reads how long its result can be");
    }

    // --- helpers ---

    /// <summary>The cap error, compared by what an administrator is told: its code and its size.</summary>
    private static void ShouldBeOutputTooLarge(ErrorOr<string> result, int maxBytes, string because = "")
    {
        var expected = TemplateErrors.OutputTooLarge(maxBytes);
        result.IsError.Should().BeTrue(because);
        result.FirstError.Code.Should().Be(expected.Code, because);
        result.FirstError.Description.Should().Be(expected.Description, because);
        result.FirstError.Metadata.Should().BeEquivalentTo(expected.Metadata, because);
    }

    private ITemplateRenderSession Session() => _renderer.BeginSession(TemplateRenderLimits.Notification);

    private (ErrorOr<string> Result, long Allocated, TimeSpan Elapsed) Measure(
        string source,
        Dictionary<string, object?> model)
    {
        var stopwatch = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = Session().Render(source, model, "en", encodeHtml: true);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (result, allocated, stopwatch.Elapsed);
    }

    private static Dictionary<string, object?> BigStrings() => new()
    {
        ["strings"] = new Dictionary<string, object?> { ["big"] = new string('x', 60_000) }
    };

    private static Dictionary<string, object?> XModel() => new() { ["x"] = "1" };

    private static string Nested(string construct, int n) => construct switch
    {
        "and" => "{{ x" + string.Concat(Enumerable.Repeat(" and x", n)) + " }}",
        "filters" => "{{ x" + string.Concat(Enumerable.Repeat(" | raw", n)) + " }}",
        "ifs" => string.Concat(Enumerable.Repeat("{% if x %}", n)) + "y" + string.Concat(Enumerable.Repeat("{% endif %}", n)),
        "fors" => string.Concat(Enumerable.Repeat("{% for i in (1..1) %}", n)) + "y" + string.Concat(Enumerable.Repeat("{% endfor %}", n)),
        "captures" => string.Concat(Enumerable.Repeat("{% capture c %}", n)) + "y" + string.Concat(Enumerable.Repeat("{% endcapture %}", n)),
        "indexers" => "{{ " + string.Concat(Enumerable.Repeat("x[", n)) + "0" + new string(']', n) + " }}",
        "elsifs" => string.Concat(Enumerable.Repeat("{% if x == 2 %}a{% elsif x == 3 %}", n)) + "y" + string.Concat(Enumerable.Repeat("{% endif %}", n)),
        _ => throw new ArgumentOutOfRangeException(nameof(construct))
    };

    /// <summary>
    /// Runs on 256 KB, the stack of an IIS worker thread: the smallest a request runs on. The
    /// renderer keeps the stack check's reserve free besides, so what the host's own frames take
    /// before the render comes out of that reserve, not out of the template's room.
    /// </summary>
    private static T OnIisStack<T>(Func<T> work) => OnStack(work, 256 * 1024);

    /// <summary>Runs on 160 KB: smaller than any host gives a request, so a refusal here is no accident of size.</summary>
    private static T OnSmallStack<T>(Func<T> work) => OnStack(work, 160 * 1024);

    private static T OnStack<T>(Func<T> work, int stackSize)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: stackSize);
        thread.Start();
        thread.Join();
        return failure is null ? result : throw new InvalidOperationException("The render threw.", failure);
    }

    private static string RenderWithUnboundedFluid(string source, Dictionary<string, object?> model)
    {
        new FluidParser().TryParse(source, out var template, out var error).Should().BeTrue(error);
        var context = new TemplateContext(model, new TemplateOptions { MaxSteps = 5_000 })
        {
            CultureInfo = CultureInfo.GetCultureInfo("en")
        };
        return template.Render(context, HtmlEncoder.Default);
    }

    /// <summary>A clock that stands still, or moves on by <see cref="Step"/> each time it is read.</summary>
    private sealed class SteppingTimeProvider : TimeProvider
    {
        private long _ticks;

        public TimeSpan Step { get; init; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks += Step.Ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
