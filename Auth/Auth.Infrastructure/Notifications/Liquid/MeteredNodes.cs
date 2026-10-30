using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;
using Fluid.Ast.BinaryExpressions;
using Fluid.Values;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Checks the budget before a statement runs. Fluid counts steps only for some statements (an
/// <c>if</c> is not one), so this is what bounds the time of a loop over conditions.
/// </summary>
internal sealed class MeteredStatement(Statement inner) : Statement
{
    public Statement Inner { get; } = inner;

    public override ValueTask<Completion> WriteToAsync(TextWriter writer, TextEncoder encoder, TemplateContext context)
    {
        RenderBudget.Of(context).Tick();
        return Inner.WriteToAsync(writer, encoder, context);
    }
}

/// <summary>
/// Checks the budget before an operator runs, so a long chain of conditions inside one
/// statement (<c>elsif</c> after <c>elsif</c>) is still stopped on time.
/// </summary>
internal sealed class MeteredExpression(Expression inner) : Expression
{
    public Expression Inner { get; } = inner;

    public override ValueTask<FluidValue> EvaluateAsync(TemplateContext context)
    {
        RenderBudget.Of(context).Tick();
        return Inner.EvaluateAsync(context);
    }
}

/// <summary>
/// <c>contains</c>, the one operator whose cost grows with the product of its operands' lengths:
/// it refuses an operand longer than a value may be, then lets Fluid's own operator compare the
/// values already evaluated, so its result is exactly Fluid's.
/// </summary>
internal sealed class BoundedContainsExpression(Expression left, Expression right) : Expression
{
    public Expression Left { get; } = left;

    public Expression Right { get; } = right;

    public override async ValueTask<FluidValue> EvaluateAsync(TemplateContext context)
    {
        var budget = RenderBudget.Of(context);
        budget.Tick();

        var leftValue = await Left.EvaluateAsync(context);
        var rightValue = await Right.EvaluateAsync(context);
        budget.EnsureValueFits(leftValue);
        budget.EnsureValueFits(rightValue);

        var evaluated = new ContainsBinaryExpression(new LiteralExpression(leftValue), new LiteralExpression(rightValue));
        return await evaluated.EvaluateAsync(context);
    }
}
