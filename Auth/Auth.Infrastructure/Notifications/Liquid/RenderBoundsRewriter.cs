using System.Runtime.CompilerServices;
using Fluid;
using Fluid.Ast;
using Fluid.Ast.BinaryExpressions;

namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>
/// Rewrites a parsed template once, before it is cached, into one that renders within a budget:
/// <list type="bullet">
/// <item>every statement in a statement list, and every operator, checks the budget first;</item>
/// <item><c>capture</c> and <c>ifchanged</c> write into bounded buffers, <c>contains</c> refuses
/// operands longer than a value may be, and a range is charged before it is built;</item>
/// <item>a tree nested past <see cref="MaxStatementDepth"/> statements or
/// <see cref="MaxExpressionDepth"/> expressions is refused: Fluid renders it by recursion, and a
/// 241-filter chain, 1.4 KB of source, overflowed a 256 KB stack.</item>
/// </list>
/// The rewritten nodes read their budget from the render's context, so the cached template is
/// the same for every purpose and holds no per-render state.
/// </summary>
/// <remarks>
/// <c>elsif</c>, <c>else</c> and <c>when</c> are left unwrapped: Fluid keeps them in typed
/// slots, and the statement that holds them is metered. A <c>for</c> loop's source, limit and
/// offset are never operators, so Fluid's check for <c>offset: continue</c> still sees what it
/// expects.
/// </remarks>
internal sealed class RenderBoundsRewriter : AstRewriter
{
    /// <summary>Deepest statement nesting, counting <c>elsif</c>, <c>else</c> and <c>when</c> as levels.</summary>
    internal const int MaxStatementDepth = 64;

    /// <summary>Deepest expression: a filter chain, an <c>and</c>/<c>or</c> chain, or nested indexers.</summary>
    internal const int MaxExpressionDepth = 32;

    private int _statementDepth;
    private int _expressionDepth;

    /// <summary>Rewrites the template, or throws <see cref="TemplateLimitException"/> when it nests too deeply.</summary>
    public static IFluidTemplate Rewrite(IFluidTemplate template) => new RenderBoundsRewriter().VisitTemplate(template);

    public override Statement Visit(Statement statement)
    {
        if (statement is null)
        {
            return null!;
        }

        Enter(ref _statementDepth, MaxStatementDepth);
        try
        {
            var rewritten = base.Visit(statement);
            return rewritten is null or ElseIfStatement or ElseStatement or WhenStatement or MeteredStatement
                ? rewritten!
                : new MeteredStatement(rewritten);
        }
        finally
        {
            _statementDepth--;
        }
    }

    public override Expression Visit(Expression expression)
    {
        if (expression is null)
        {
            return null!;
        }

        Enter(ref _expressionDepth, MaxExpressionDepth);
        try
        {
            var rewritten = base.Visit(expression);
            return rewritten is BinaryExpression binary ? new MeteredExpression(binary) : rewritten;
        }
        finally
        {
            _expressionDepth--;
        }
    }

    protected override Statement VisitCaptureStatement(CaptureStatement captureStatement)
    {
        TryRewriteStatements(captureStatement.Statements, out var statements);
        return new BoundedCaptureStatement(captureStatement.Identifier, statements);
    }

    protected override Statement VisitIfChangedStatement(IfChangedStatement ifChangedStatement)
    {
        TryRewriteStatements(ifChangedStatement.Statements, out var statements);
        return new BoundedIfChangedStatement(statements);
    }

    protected override Expression VisitContainsBinaryExpression(ContainsBinaryExpression containsBinaryExpression)
    {
        TryRewriteExpression(containsBinaryExpression.Left, out var left);
        TryRewriteExpression(containsBinaryExpression.Right, out var right);
        return new BoundedContainsExpression(left, right);
    }

    protected override Expression VisitRangeExpression(RangeExpression rangeExpression)
    {
        TryRewriteExpression(rangeExpression.From, out var from);
        TryRewriteExpression(rangeExpression.To, out var to);
        return new BoundedRangeExpression(from, to);
    }

    /// <summary>
    /// Fluid's rewriter leaves member expressions as they are; an indexer holds an expression
    /// (<c>a[(1..9)]</c>, <c>a[b[c]]</c>), so its segments are rewritten and counted too.
    /// </summary>
    protected override Expression VisitMemberExpression(MemberExpression memberExpression)
    {
        var updated = false;
        var segments = new List<MemberSegment>(memberExpression.Segments.Count);
        foreach (var segment in memberExpression.Segments)
        {
            if (segment is IndexerSegment indexer && TryRewriteExpression(indexer.Expression, out var index))
            {
                segments.Add(new IndexerSegment(index));
                updated = true;
            }
            else
            {
                segments.Add(segment);
            }
        }

        return updated ? new MemberExpression(segments) : memberExpression;
    }

    private static void Enter(ref int depth, int max)
    {
        if (++depth > max || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new TemplateLimitException(TemplateLimit.TooComplex);
        }
    }
}
