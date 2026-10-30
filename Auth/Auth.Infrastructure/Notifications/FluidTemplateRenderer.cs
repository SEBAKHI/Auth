using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Infrastructure.Notifications.Liquid;
using ErrorOr;
using Fluid;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// Sandboxed Liquid renderer built on Fluid: variables resolve exclusively from
/// the supplied model dictionary (no reflection over CLR objects), execution is
/// bounded, and HTML contexts encode every variable. Parsed templates are
/// cached because parsing dominates render cost and IFluidTemplate is thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Every render is bounded while it runs, so a short template cannot make the server allocate,
/// recurse or loop in proportion to what it asks for: the output stops at its cap
/// (<see cref="Liquid.BoundedTextWriter"/>), what a template builds is charged to its session's
/// budget (<see cref="Liquid.RenderBudget"/>, <see cref="Liquid.BoundedFilters"/>), nesting is
/// counted while parsing (<see cref="Liquid.BoundedFluidParser"/>) and the parsed tree is
/// rewritten to check the budget as it renders (<see cref="Liquid.RenderBoundsRewriter"/>).
/// </para>
/// <para>
/// Fluid still counts its own steps (<see cref="MaxSteps"/>), and a step limit it reaches is
/// reported as <c>Notification.RenderFailed</c>, as before.
/// </para>
/// </remarks>
public class FluidTemplateRenderer : ITemplateRenderer
{
    private const int MaxSteps = 5_000;
    private const int ParsedCacheCapacity = 1_024;

    /// <summary>
    /// Source characters the parse cache holds at most (16 MB): previews put any source into it,
    /// so a count alone would let 1,024 long previews pin a gigabyte.
    /// </summary>
    private const long ParsedCacheMaxSourceChars = 8 * 1024 * 1024;

    /// <summary>Fluid's scope nesting (loops, the render's own scope); above the parse limit, so ours applies first.</summary>
    private const int MaxRecursion = 64;

    private static readonly BoundedFluidParser Parser = new();

    private readonly ConcurrentDictionary<string, IFluidTemplate> _parsedCache = new(StringComparer.Ordinal);
    private readonly TemplateOptions _options;
    private readonly TimeProvider _timeProvider;
    private long _cachedSourceChars;

    public FluidTemplateRenderer(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _options = new TemplateOptions
        {
            MaxSteps = MaxSteps,
            MaxRecursion = MaxRecursion
        };
        BoundedFilters.Bound(_options.Filters);
    }

    /// <summary>Templates the parse cache holds.</summary>
    internal int CachedTemplateCount => _parsedCache.Count;

    /// <summary>Source characters the parse cache holds, as it counts them.</summary>
    internal long CachedSourceCharacters => Interlocked.Read(ref _cachedSourceChars);

    /// <inheritdoc />
    public ErrorOr<Success> Validate(string source)
    {
        var parsed = Parse(source);
        return parsed.IsError ? parsed.Errors : Result.Success;
    }

    /// <inheritdoc />
    public ITemplateRenderSession BeginSession(TemplateRenderLimits limits) =>
        new Session(this, new RenderBudget(limits, _timeProvider));

    /// <summary>The bounded template for the source, parsed and rewritten once and then cached.</summary>
    private ErrorOr<IFluidTemplate> Parse(string source)
    {
        if (_parsedCache.TryGetValue(source, out var cached))
        {
            return ErrorOrFactory.From(cached);
        }

        IFluidTemplate template;
        try
        {
            if (!Parser.TryParse(source, out var parsed, out var parseError))
            {
                return NotificationErrors.InvalidTemplateSyntax(parseError ?? string.Empty);
            }

            template = RenderBoundsRewriter.Rewrite(parsed!);
        }
        catch (TemplateLimitException)
        {
            return TemplateErrors.TooComplex;
        }

        // Bounded cache: the working set is small (translations × templates plus layouts);
        // clear-and-restart on overflow, by count or by the source it holds, keeps it simple.
        if (_parsedCache.Count >= ParsedCacheCapacity
            || Interlocked.Add(ref _cachedSourceChars, source.Length) > ParsedCacheMaxSourceChars)
        {
            _parsedCache.Clear();
            Interlocked.Exchange(ref _cachedSourceChars, source.Length);
        }

        _parsedCache[source] = template;
        return ErrorOrFactory.From(template);
    }

    private static CultureInfo ResolveCulture(string languageCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageCode);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>One render operation: the renders it makes share one <see cref="RenderBudget"/>.</summary>
    private sealed class Session(FluidTemplateRenderer renderer, RenderBudget budget) : ITemplateRenderSession
    {
        public ErrorOr<string> Render(
            string source,
            IReadOnlyDictionary<string, object?> model,
            string languageCode,
            bool encodeHtml)
        {
            return RenderCore(source, model, languageCode, encodeHtml, trackUnresolved: false, out _);
        }

        public ErrorOr<string> RenderTracking(
            string source,
            IReadOnlyDictionary<string, object?> model,
            string languageCode,
            bool encodeHtml,
            out IReadOnlyList<string> unresolvedVariables)
        {
            return RenderCore(source, model, languageCode, encodeHtml, trackUnresolved: true, out unresolvedVariables);
        }

        private ErrorOr<string> RenderCore(
            string source,
            IReadOnlyDictionary<string, object?> model,
            string languageCode,
            bool encodeHtml,
            bool trackUnresolved,
            out IReadOnlyList<string> unresolvedVariables)
        {
            unresolvedVariables = [];

            var template = renderer.Parse(source);
            if (template.IsError)
            {
                return template.Errors;
            }

            var trackingModel = new TrackingModel(model);
            var context = new BudgetedTemplateContext(trackingModel, renderer._options, budget)
            {
                CultureInfo = ResolveCulture(languageCode)
            };

            budget.BeginRender();
            try
            {
                budget.AdmitModel(model);

                var encoder = encodeHtml ? (TextEncoder)HtmlEncoder.Default : NullEncoder.Default;
                using var writer = BoundedTextWriter.ForOutput(budget);
                var render = template.Value.RenderAsync(writer, context, encoder);
                if (!render.IsCompletedSuccessfully)
                {
                    render.AsTask().GetAwaiter().GetResult();
                }

                var rendered = writer.ToString();

                // The writer counts as it goes; this recount is exact for lone surrogates too.
                if (Encoding.UTF8.GetByteCount(rendered) > budget.MaxOutputBytes)
                {
                    return TemplateErrors.OutputTooLarge(budget.MaxOutputBytes);
                }

                if (trackUnresolved)
                {
                    unresolvedVariables = trackingModel.MissingKeys;
                }

                return rendered;
            }
            catch (TemplateLimitException limit)
            {
                return limit.Limit == TemplateLimit.OutputTooLarge
                    ? TemplateErrors.OutputTooLarge(budget.MaxOutputBytes)
                    : TemplateErrors.TooComplex;
            }
            catch (Exception ex)
            {
                return NotificationErrors.RenderFailed(ex.Message);
            }
            finally
            {
                budget.EndRender();
            }
        }
    }

    /// <summary>
    /// Dictionary model that records lookups of keys it does not contain, so
    /// publish-time validation can report variables missing from the catalog's
    /// sample data. Fluid resolves root identifiers of dictionary models through
    /// TryGetValue, which is the interception point.
    /// </summary>
    private sealed class TrackingModel : IDictionary<string, object?>
    {
        private readonly IReadOnlyDictionary<string, object?> _inner;
        private readonly HashSet<string> _missing = [];

        public TrackingModel(IReadOnlyDictionary<string, object?> inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<string> MissingKeys => _missing.Order(StringComparer.Ordinal).ToList();

        public bool TryGetValue(string key, out object? value)
        {
            if (_inner.TryGetValue(key, out value))
            {
                return true;
            }

            _missing.Add(key);
            return false;
        }

        public bool ContainsKey(string key)
        {
            if (_inner.ContainsKey(key))
            {
                return true;
            }

            _missing.Add(key);
            return false;
        }

        public object? this[string key]
        {
            get => TryGetValue(key, out var value)
                ? value
                : throw new KeyNotFoundException(key);
            set => throw new NotSupportedException();
        }

        public ICollection<string> Keys => _inner.Keys.ToList();
        public ICollection<object?> Values => _inner.Values.ToList();
        public int Count => _inner.Count;
        public bool IsReadOnly => true;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(KeyValuePair<string, object?> item) =>
            _inner.TryGetValue(item.Key, out var value) && Equals(value, item.Value);

        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        {
            foreach (var pair in _inner)
            {
                array[arrayIndex++] = pair;
            }
        }

        public void Add(string key, object? value) => throw new NotSupportedException();
        public void Add(KeyValuePair<string, object?> item) => throw new NotSupportedException();
        public bool Remove(string key) => throw new NotSupportedException();
        public bool Remove(KeyValuePair<string, object?> item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
}
