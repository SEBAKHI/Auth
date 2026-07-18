using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Fluid;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// Sandboxed Liquid renderer built on Fluid: variables resolve exclusively from
/// the supplied model dictionary (no reflection over CLR objects), execution is
/// step-bounded, and HTML contexts encode every variable. Parsed templates are
/// cached because parsing dominates render cost and IFluidTemplate is thread-safe.
/// </summary>
public class FluidTemplateRenderer : ITemplateRenderer
{
    private const int MaxSteps = 5_000;
    private const int ParsedCacheCapacity = 1_024;

    private static readonly FluidParser Parser = new();
    private readonly ConcurrentDictionary<string, IFluidTemplate> _parsedCache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ErrorOr<string> Render(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml)
    {
        return RenderCore(source, model, languageCode, encodeHtml, trackUnresolved: false, out _);
    }

    /// <inheritdoc />
    public ErrorOr<Success> Validate(string source)
    {
        if (Parser.TryParse(source, out _, out var error))
        {
            return Result.Success;
        }

        return NotificationErrors.InvalidTemplateSyntax(error);
    }

    /// <inheritdoc />
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

        IFluidTemplate template;
        if (_parsedCache.TryGetValue(source, out var cached))
        {
            template = cached;
        }
        else
        {
            if (!Parser.TryParse(source, out template!, out var parseError))
            {
                return NotificationErrors.InvalidTemplateSyntax(parseError);
            }

            // Bounded cache: the working set is small (translations × templates
            // plus layouts); clear-and-restart on overflow keeps it simple.
            if (_parsedCache.Count >= ParsedCacheCapacity)
            {
                _parsedCache.Clear();
            }

            _parsedCache[source] = template;
        }

        var options = new TemplateOptions
        {
            MaxSteps = MaxSteps,
            CultureInfo = ResolveCulture(languageCode)
        };

        var trackingModel = new TrackingModel(model);
        var context = new TemplateContext(trackingModel, options);

        try
        {
            var encoder = encodeHtml ? (TextEncoder)HtmlEncoder.Default : NullEncoder.Default;
            var rendered = template.Render(context, encoder);

            if (trackUnresolved)
            {
                unresolvedVariables = trackingModel.MissingKeys;
            }

            return rendered;
        }
        catch (Exception ex)
        {
            return NotificationErrors.RenderFailed(ex.Message);
        }
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
