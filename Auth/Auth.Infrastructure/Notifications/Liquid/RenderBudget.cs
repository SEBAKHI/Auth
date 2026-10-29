using System.Collections;
using System.Runtime.CompilerServices;
using Auth.Application.Interfaces;
using Fluid;
using Fluid.Values;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// The budget of one render session: the output cap of each render, the longest value any
/// render may build or read, the work all of them may do, and the time they may take.
/// Everything a template builds is charged here before or as it is built, so memory stays a
/// small multiple of the output cap whatever the template asks for.
/// </summary>
/// <remarks>
/// <para>
/// Units: the output cap counts UTF-8 bytes after encoding (what is sent). Values and work count
/// UTF-16 characters (what is held in memory); an array element weighs
/// <see cref="ArrayElementWeight"/> characters, a reference and a small object.
/// </para>
/// <para>
/// Time counts only while a render runs, not the I/O a caller does between the parts of one
/// composition, so a slow database never turns a legitimate template into a refused one.
/// </para>
/// </remarks>
internal sealed class RenderBudget
{
    /// <summary>The work of a session, as a multiple of its output cap.</summary>
    internal const int WorkPerOutputByte = 8;

    /// <summary>The weight of one array element, in characters.</summary>
    internal const int ArrayElementWeight = 16;

    /// <summary>Deepest model a render reads; the JSON a sample comes from stops at 64 too.</summary>
    private const int MaxModelDepth = 64;

    /// <summary>Render time a session may spend; legitimate templates take milliseconds.</summary>
    internal static readonly TimeSpan RenderTime = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly long _renderTicks;
    private long _work;
    private long _spentTicks;
    private long _renderStartedAt;

    public RenderBudget(TemplateRenderLimits limits, TimeProvider time)
    {
        MaxOutputBytes = limits.MaxOutputBytes;
        MaxValueChars = limits.MaxOutputBytes;
        MaxWork = (long)limits.MaxOutputBytes * WorkPerOutputByte;
        _time = time;
        _renderTicks = (long)(RenderTime.TotalSeconds * time.TimestampFrequency);
    }

    /// <summary>Largest output of one render, in UTF-8 bytes.</summary>
    public int MaxOutputBytes { get; }

    /// <summary>
    /// Longest string a render may build or read, in characters: the output cap, since a longer
    /// string could never be written whole (a character is at least one UTF-8 byte).
    /// </summary>
    public int MaxValueChars { get; }

    /// <summary>Characters all renders of the session may build, output included.</summary>
    public long MaxWork { get; }

    /// <summary>The budget a render's context carries.</summary>
    public static RenderBudget Of(TemplateContext context) => ((BudgetedTemplateContext)context).Budget;

    public void BeginRender() => _renderStartedAt = _time.GetTimestamp();

    public void EndRender() => _spentTicks += _time.GetTimestamp() - _renderStartedAt;

    /// <summary>
    /// Called before every statement, operator, filter and write: stops a render that ran out of
    /// time, or whose stack is nearly spent (a backstop under the nesting limits).
    /// </summary>
    public void Tick()
    {
        if (_spentTicks + (_time.GetTimestamp() - _renderStartedAt) > _renderTicks
            || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }
    }

    /// <summary>Charges characters built by the render.</summary>
    public void Charge(long characters)
    {
        _work += characters;
        if (_work > MaxWork)
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }
    }

    /// <summary>Charges an array before it is built.</summary>
    public void ChargeArray(long elements) => Charge(elements * ArrayElementWeight);

    /// <summary>Refuses an operation whose result would be longer than a value may be.</summary>
    public void EnsureValueFits(long characters)
    {
        if (characters > MaxValueChars)
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }
    }

    /// <summary>Refuses a string longer than a value may be, before an operation reads it.</summary>
    public void EnsureValueFits(FluidValue value)
    {
        if (value.Type == FluidValues.String)
        {
            EnsureValueFits(value.ToStringValue().Length);
        }
    }

    /// <summary>
    /// Charges what a filter returned, unless it handed back a string it was given (raw,
    /// default, an untouched strip): only what the filter built costs anything.
    /// </summary>
    public FluidValue Admit(FluidValue result, FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (result.Type == FluidValues.String)
        {
            var text = result.ToStringValue();
            if (!IsGiven(text, input, arguments))
            {
                EnsureValueFits(text.Length);
                Charge(text.Length);
            }
        }
        else if (result.Type == FluidValues.Array)
        {
            Charge(Weigh(result, context));
        }

        return result;
    }

    /// <summary>
    /// Refuses a model holding a string longer than a value may be, or a list heavier than the
    /// whole work budget, before the render can encode or search it.
    /// </summary>
    public void AdmitModel(IReadOnlyDictionary<string, object?> model)
    {
        foreach (var value in model.Values)
        {
            AdmitModelValue(value, depth: 1);
        }
    }

    private void AdmitModelValue(object? value, int depth)
    {
        if (depth > MaxModelDepth)
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }

        if (value is string text)
        {
            EnsureValueFits(text.Length);
            return;
        }

        IEnumerable? children = value switch
        {
            IReadOnlyDictionary<string, object?> map => map.Values,
            IDictionary<string, object?> map => map.Values,
            IEnumerable items => items,
            _ => null
        };

        if (children is null)
        {
            return;
        }

        var count = 0L;
        foreach (var child in children)
        {
            if (++count * ArrayElementWeight > MaxWork)
            {
                throw new TemplateLimitException(TemplateLimit.TooComplex);
            }

            AdmitModelValue(child, depth + 1);
        }
    }

    private static bool IsGiven(string text, FluidValue input, FilterArguments arguments)
    {
        if (input.Type == FluidValues.String && ReferenceEquals(text, input.ToStringValue()))
        {
            return true;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments.At(i);
            if (argument.Type == FluidValues.String && ReferenceEquals(text, argument.ToStringValue()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An array weighs its elements and the strings they hold, shared ones included: an array of
    /// one long string repeated is as heavy to sort, join or write as the strings it repeats.
    /// </summary>
    private static long Weigh(FluidValue array, TemplateContext context)
    {
        var weight = 0L;
        foreach (var element in array.Enumerate(context))
        {
            weight += ArrayElementWeight;
            if (element.Type == FluidValues.String)
            {
                weight += element.ToStringValue().Length;
            }
            else if (element.Type == FluidValues.Array)
            {
                weight += Weigh(element, context);
            }
        }

        return weight;
    }
}

/// <summary>A Fluid context that carries the budget of its session to the bounded nodes and filters.</summary>
internal sealed class BudgetedTemplateContext(object model, TemplateOptions options, RenderBudget budget)
    : TemplateContext(model, options)
{
    public RenderBudget Budget { get; } = budget;
}
