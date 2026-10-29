using Fluid;
using Fluid.Ast;
using Fluid.Values;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Fluid's <c>(from..to)</c>, charging the array before building it. Fluid builds every element
/// of a range up front, even for a loop that breaks at once, so <c>(1..100000000)</c> allocated
/// gigabytes in one step. Everything else is Fluid's <c>RangeExpression</c> as it is in 2.40.0;
/// the length is computed in 64 bits, so a range whose length overflows is refused, not emptied.
/// </summary>
internal sealed class BoundedRangeExpression(Expression from, Expression to) : Expression
{
    public Expression From { get; } = from;

    public Expression To { get; } = to;

    public override async ValueTask<FluidValue> EvaluateAsync(TemplateContext context)
    {
        var budget = RenderBudget.Of(context);
        budget.Tick();

        var startTask = From.EvaluateAsync(context);
        var endTask = To.EvaluateAsync(context);
        var start = Convert.ToInt32((await startTask).ToNumberValue());
        var end = Convert.ToInt32((await endTask).ToNumberValue());

        var length = Math.Max(0L, (long)end - start + 1);
        budget.ChargeArray(length);

        var list = new FluidValue[length];
        for (var i = 0; i < list.Length; i++)
        {
            list[i] = NumberValue.Create(start + i);
        }

        return new ArrayValue(list);
    }
}
