using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Fluid's <c>{% ifchanged %}</c>, rendering its body into a bounded writer instead of an
/// unbounded buffer. Everything else is Fluid's <c>IfChangedStatement</c> as it is in 2.40.0,
/// the shared register key included.
/// </summary>
internal sealed class BoundedIfChangedStatement(IReadOnlyList<Statement> statements) : Statement
{
    private const string IfChangedRegisterKey = "$$ifchanged$$";

    public IReadOnlyList<Statement> Statements { get; } = statements;

    public override async ValueTask<Completion> WriteToAsync(TextWriter writer, TextEncoder encoder, TemplateContext context)
    {
        context.IncrementSteps();

        context.AmbientValues.TryGetValue(IfChangedRegisterKey, out var previousValueObj);
        var previousValue = previousValueObj as string;

        using var buffer = BoundedTextWriter.ForValue(RenderBudget.Of(context));
        var completion = Completion.Normal;

        for (var i = 0; i < Statements.Count; i++)
        {
            completion = await Statements[i].WriteToAsync(buffer, encoder, context);

            if (completion != Completion.Normal)
            {
                break;
            }
        }

        var currentValue = buffer.ToString();

        if (!string.Equals(previousValue, currentValue, StringComparison.Ordinal))
        {
            context.AmbientValues[IfChangedRegisterKey] = currentValue;
            writer.Write(currentValue);
        }

        return completion;
    }
}
