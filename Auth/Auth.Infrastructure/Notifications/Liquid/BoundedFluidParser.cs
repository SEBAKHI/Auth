using System.Runtime.CompilerServices;
using Fluid;
using Fluid.Parser;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Fluid's parser with its nesting counted. Fluid parses nested blocks (<c>{% if %}</c> inside
/// <c>{% if %}</c>) and nested indexers by recursion, and nothing bounds it: 68 nested
/// <c>{% if %}</c>, 1.4 KB of source, overflowed a 256 KB stack, and a stack overflow ends the
/// process for every user, not the request. Each parse counts how deep it is inside the
/// recursive parsers and stops past <see cref="MaxNesting"/>.
/// </summary>
/// <remarks>
/// The count lives on the parse's own context (Parlot calls <c>OnEnterParser</c> and
/// <c>OnExitParser</c> on it around every deferred parser), so the parser is shared and holds no
/// per-parse state.
/// </remarks>
internal sealed class BoundedFluidParser : FluidParser
{
    /// <summary>
    /// Deepest a parse may be inside the recursive parsers: the template's own statement list,
    /// then one level per nested block or indexer, so 24 nested blocks. Legitimate templates nest
    /// a handful; the deepest allowed renders with room to spare on a 256 KB stack.
    /// </summary>
    internal const int MaxNesting = 25;

    /// <summary>
    /// Parses the source as <c>FluidParser.TryParse</c> does, with the same error message, or
    /// throws <see cref="TemplateLimitException"/> when it nests past <see cref="MaxNesting"/>.
    /// </summary>
    public bool TryParse(string source, out IFluidTemplate? template, out string? error)
    {
        template = null;
        error = null;

        var context = new NestingParseContext(source, this);
        try
        {
            if (!Grammar.TryParse(context, out var statements, out var parlotError))
            {
                error = parlotError is null ? "The template could not be parsed." : Describe(parlotError, source);
                return false;
            }

            template = new FluidTemplate(statements);
            return true;
        }
        catch (TemplateLimitException)
        {
            throw;
        }
        catch (InsufficientExecutionStackException)
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool IsRecursive(object parser) =>
        ReferenceEquals(parser, AnyTagsList)
        || ReferenceEquals(parser, KnownTagsList)
        || ReferenceEquals(parser, Primary)
        || ReferenceEquals(parser, FilterExpression);

    /// <summary>The message <c>FluidParserExtensions.Parse</c> builds: the error and its source line.</summary>
    private static string Describe(Parlot.ParseError parlotError, string template)
    {
        var start = parlotError.Position.Offset - 1;
        var end = parlotError.Position.Offset;
        while (start > 0 && template[start] != '\n' && template[start] != '\r')
        {
            start--;
        }

        while (end < template.Length && template[end] != '\n')
        {
            end++;
        }

        var line = template.Substring(start, end - start).Trim('\n', '\r');
        return $"{parlotError.Message} at {parlotError.Position}\nSource:\n{line}";
    }

    private sealed class NestingParseContext : FluidParseContext
    {
        private int _depth;

        public NestingParseContext(string text, BoundedFluidParser parser) : base(text)
        {
            OnEnterParser = (entered, _) =>
            {
                if (!parser.IsRecursive(entered))
                {
                    return;
                }

                if (++_depth > MaxNesting || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
                {
                    throw new TemplateLimitException(TemplateLimit.TooComplex);
                }
            };

            OnExitParser = (exited, _) =>
            {
                if (parser.IsRecursive(exited))
                {
                    _depth--;
                }
            };
        }
    }
}
