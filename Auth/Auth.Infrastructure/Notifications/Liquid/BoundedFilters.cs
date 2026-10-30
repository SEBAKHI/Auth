using System.Collections.Frozen;
using Fluid;
using Fluid.Values;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Wraps every filter of a <see cref="FilterCollection"/> so that it checks the budget, refuses a
/// string input or argument longer than a value may be, and has its result charged.
/// </summary>
/// <remarks>
/// <para>
/// Most filters build a result at most a few times longer than what they are given (escape,
/// url_encode, newline_to_br, append), so checking the result after it is built keeps memory a
/// small multiple of the longest value. Five can build far more than they are given, from an
/// argument a template chooses: those are estimated first, and refused before they run when
/// their result would be longer than a value may be (<see cref="Estimators"/>).
/// </para>
/// <para>
/// <see cref="ReviewedFilters"/> names every filter Fluid 2.40.0 registers, each read for how
/// long its result can be. A Fluid upgrade that adds one fails a test until it is read too.
/// </para>
/// </remarks>
internal static class BoundedFilters
{
    /// <summary>Characters one strftime or numeric conversion writes at most, besides its width.</summary>
    private const int ConversionChars = 128;

    /// <summary>The filters Fluid 2.40.0 registers by default, each reviewed for its result's length.</summary>
    internal static readonly FrozenSet<string> ReviewedFilters = FrozenSet.ToFrozenSet(
    [
        // Arrays: a result no longer than the input, or a number or element.
        "join", "first", "last", "concat", "map", "reverse", "size", "sort", "sort_natural",
        "uniq", "where", "sum", "find", "find_index", "has", "reject",
        // Strings: at most the input and the arguments, a few times over.
        "append", "capitalize", "downcase", "lstrip", "rstrip", "newline_to_br", "prepend",
        "remove_first", "remove", "remove_last", "replace_first", "replace", "replace_last",
        "slice", "split", "strip", "strip_newlines", "truncate", "truncatewords", "upcase",
        // Numbers.
        "abs", "at_least", "at_most", "ceil", "divided_by", "floor", "minus", "modulo", "plus",
        "round", "times",
        // Miscellaneous: encodings, hashes, dates and formats.
        "default", "date", "raw", "compact", "url_encode", "url_decode", "base64_encode",
        "base64_decode", "base64_url_safe_encode", "base64_url_safe_decode", "strip_html",
        "escape", "escape_once", "handle", "handleize", "json", "time_zone", "format_number",
        "format_string", "format_date", "md5", "sha1", "sha256", "hmac_sha1", "hmac_sha256",
    ]);

    /// <summary>
    /// The filters whose result can be far longer than their input, each with an upper bound on
    /// its result's length computed without building it.
    /// </summary>
    private static readonly FrozenDictionary<string, Func<FluidValue, FilterArguments, TemplateContext, long>> Estimators =
        new Dictionary<string, Func<FluidValue, FilterArguments, TemplateContext, long>>(StringComparer.Ordinal)
        {
            // "%100000000n" pads with a width the template picks.
            ["date"] = (_, arguments, _) => EstimateStrftime(arguments.At(0)),
            // "F100000000" is a precision of a hundred million digits.
            ["format_number"] = (_, arguments, _) => EstimateNumberFormat(arguments.At(0).ToStringValue()),
            // "{0,999999}" and "{0:F100000000}" pad and widen each item.
            ["format_string"] = EstimateCompositeFormat,
            // Each occurrence of the search string becomes the replacement.
            ["replace"] = EstimateReplace,
            // Each element is followed by the separator.
            ["join"] = EstimateJoin,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Replaces every filter of the collection with its bounded form.</summary>
    public static void Bound(FilterCollection filters)
    {
        foreach (var (name, filter) in filters.ToArray())
        {
            filters.AddFilter(name, Bound(filter, Estimators.GetValueOrDefault(name)));
        }
    }

    private static FilterDelegate Bound(FilterDelegate filter, Func<FluidValue, FilterArguments, TemplateContext, long>? estimate)
    {
        return (input, arguments, context) =>
        {
            var budget = RenderBudget.Of(context);
            budget.Tick();
            budget.EnsureValueFits(input);
            for (var i = 0; i < arguments.Count; i++)
            {
                budget.EnsureValueFits(arguments.At(i));
            }

            if (estimate is not null)
            {
                budget.EnsureValueFits(estimate(input, arguments, context));
            }

            var result = filter(input, arguments, context);
            return result.IsCompletedSuccessfully
                ? new ValueTask<FluidValue>(budget.Admit(result.Result, input, arguments, context))
                : Awaited(result, budget, input, arguments, context);
        };

        static async ValueTask<FluidValue> Awaited(
            ValueTask<FluidValue> pending,
            RenderBudget budget,
            FluidValue input,
            FilterArguments arguments,
            TemplateContext context) =>
            budget.Admit(await pending, input, arguments, context);
    }

    /// <summary>
    /// Fluid's strftime grammar: after <c>%</c>, flags (<c>^ - _ :</c>) and width digits in any
    /// order, then one conversion. A conversion writes its width plus at most
    /// <see cref="ConversionChars"/>; any other character writes itself.
    /// </summary>
    private static long EstimateStrftime(FluidValue formatArgument)
    {
        if (formatArgument.IsNil())
        {
            return 0;
        }

        var estimate = 0L;
        var percent = false;
        var width = 0L;
        foreach (var c in formatArgument.ToStringValue())
        {
            if (!percent)
            {
                percent = c == '%';
                estimate += percent ? 0 : 1;
                continue;
            }

            if (c is '^' or '-' or '_' or ':')
            {
                continue;
            }

            // Fluid accepts any Unicode digit here and adds (c - '0'); an Arabic digit makes a
            // wide width there too.
            if (char.IsDigit(c))
            {
                width = Math.Min(width * 10 + (c - '0'), int.MaxValue);
                continue;
            }

            estimate += Math.Max(0, width) + ConversionChars;
            percent = false;
            width = 0;
        }

        return estimate;
    }

    /// <summary>
    /// A .NET numeric format: a standard format (a letter, then up to nine digits) writes its
    /// precision in digits plus the number; a custom format writes about its own length plus the
    /// number's digits.
    /// </summary>
    private static long EstimateNumberFormat(string format) =>
        format.Length + StandardPrecision(format) + ConversionChars;

    private static long StandardPrecision(string format)
    {
        if (format.Length < 2 || !char.IsAsciiLetter(format[0]))
        {
            return 0;
        }

        var precision = 0L;
        foreach (var c in format.AsSpan(1))
        {
            if (!char.IsAsciiDigit(c))
            {
                return 0;
            }

            precision = Math.Min(precision * 10 + (c - '0'), int.MaxValue);
        }

        return precision;
    }

    /// <summary>
    /// A .NET composite format (the input) with the filter's arguments: each format item writes
    /// at most its alignment, the longest argument, and its own format's estimate.
    /// </summary>
    private static long EstimateCompositeFormat(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil())
        {
            return 0;
        }

        var longestArgument = 0L;
        for (var i = 0; i < arguments.Count; i++)
        {
            longestArgument = Math.Max(longestArgument, arguments.At(i).ToStringValue().Length);
        }

        var format = input.ToStringValue();
        var estimate = (long)format.Length;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '{')
            {
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '{')
            {
                i++;
                continue;
            }

            var close = format.IndexOf('}', i + 1);
            var item = close < 0 ? format.AsSpan(i + 1) : format.AsSpan(i + 1, close - i - 1);
            estimate += longestArgument + EstimateFormatItem(item);
            i = close < 0 ? format.Length : close;
        }

        return estimate;
    }

    /// <summary><c>index[,alignment][:format]</c>: the alignment's width plus the format's estimate.</summary>
    private static long EstimateFormatItem(ReadOnlySpan<char> item)
    {
        var colon = item.IndexOf(':');
        var head = colon < 0 ? item : item[..colon];
        var itemFormat = colon < 0 ? ReadOnlySpan<char>.Empty : item[(colon + 1)..];

        var alignment = 0L;
        var comma = head.IndexOf(',');
        if (comma >= 0)
        {
            foreach (var c in head[(comma + 1)..])
            {
                if (char.IsAsciiDigit(c))
                {
                    alignment = Math.Min(alignment * 10 + (c - '0'), int.MaxValue);
                }
            }
        }

        return alignment + EstimateNumberFormat(itemFormat.ToString());
    }

    /// <summary>The exact length <c>string.Replace</c> returns, counted without building it.</summary>
    private static long EstimateReplace(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        var value = input.ToStringValue();
        var search = arguments.At(0).ToStringValue();
        var replacement = arguments.At(1).ToStringValue();
        if (search.Length == 0)
        {
            // string.Replace refuses an empty search string; Fluid reports its error.
            return 0;
        }

        var occurrences = 0L;
        for (var at = value.IndexOf(search, StringComparison.Ordinal);
             at >= 0;
             at = value.IndexOf(search, at + search.Length, StringComparison.Ordinal))
        {
            occurrences++;
        }

        return value.Length + occurrences * (replacement.Length - search.Length);
    }

    /// <summary>The exact length <c>join</c> returns: every element's text and a separator between each.</summary>
    private static long EstimateJoin(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.Type != FluidValues.Array)
        {
            return 0;
        }

        var separator = arguments.At(0).ToStringValue();
        var length = 0L;
        var count = 0L;
        foreach (var element in input.Enumerate(context))
        {
            length += element.ToStringValue().Length;
            count++;
        }

        return length + Math.Max(0, count - 1) * separator.Length;
    }
}
