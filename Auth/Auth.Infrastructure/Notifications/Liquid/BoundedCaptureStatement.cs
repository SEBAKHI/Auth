using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;
using Fluid.Values;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Fluid's <c>{% capture %}</c>, writing its body into a bounded writer instead of an unbounded
/// buffer: a capture of a loop is stopped at the longest value, not built and measured after.
/// Everything else is Fluid's <c>CaptureStatement</c> as it is in 2.40.0.
/// </summary>
internal sealed class BoundedCaptureStatement(string identifier, IReadOnlyList<Statement> statements) : Statement
{
    public string Identifier { get; } = identifier;

    public IReadOnlyList<Statement> Statements { get; } = statements;

    public override async ValueTask<Completion> WriteToAsync(TextWriter writer, TextEncoder encoder, TemplateContext context)
    {
        var completion = Completion.Normal;

        using var buffer = BoundedTextWriter.ForValue(RenderBudget.Of(context));
        for (var i = 0; i < Statements.Count; i++)
        {
            completion = await Statements[i].WriteToAsync(buffer, encoder, context);

            if (completion != Completion.Normal)
            {
                break;
            }
        }

        FluidValue result = new StringValue(buffer.ToString(), false);

        if (context.Captured != null)
        {
            result = await context.Captured.Invoke(Identifier, result, context);
        }

        context.SetValue(Identifier, result);

        return completion;
    }
}
