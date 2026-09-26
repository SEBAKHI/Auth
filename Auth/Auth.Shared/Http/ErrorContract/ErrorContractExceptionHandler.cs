using System.Collections.Frozen;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// The one exception handler (ADR 0001, section 5). A translated exception or a dependency
/// outage is written here; anything else is left to the framework, which answers 500 with
/// <see cref="TransportErrorCodes.Unexpected"/> and logs the exception. No exception text ever
/// reaches the body: <c>detail</c> is the sentence of the code.
/// </summary>
/// <remarks>
/// A client that hangs up never gets here: for an <see cref="OperationCanceledException"/> or
/// <see cref="IOException"/> with <c>RequestAborted</c> cancelled, the middleware logs at Debug,
/// sets 499 and returns before any <see cref="IExceptionHandler"/> runs.
/// </remarks>
public sealed class ErrorContractExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problems;
    private readonly FrozenDictionary<Type, IExceptionProblemTranslator> _translators;
    private readonly ILogger<ErrorContractExceptionHandler> _logger;

    public ErrorContractExceptionHandler(
        IProblemDetailsService problems,
        IEnumerable<IExceptionProblemTranslator> translators,
        ILogger<ErrorContractExceptionHandler> logger)
    {
        _problems = problems;
        _translators = translators.ToFrozenDictionary(translator => translator.ExceptionType);
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // A code recorded before the request failed describes a response that is no longer
        // being written; left in place it would label the 500 or 503 below.
        httpContext.Items.Remove(ProblemItems.Code);
        httpContext.Items.Remove(ProblemItems.Args);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var problem = Translate(exception);
        if (problem is null)
        {
            return false;
        }

        // The middleware no longer logs an exception that a handler handles (.NET 10).
        _logger.Log(
            problem.Status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            "Exception answered with {StatusCode} {ErrorCode}",
            problem.Status,
            problem.Code ?? TransportErrorCodes.For(problem.Status));

        httpContext.Response.StatusCode = problem.Status;
        if (problem.Code is not null)
        {
            httpContext.Items[ProblemItems.Code] = problem.Code;
        }

        return await _problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = problem.Status },
        });
    }

    private ExceptionProblem? Translate(Exception exception)
    {
        if (_translators.TryGetValue(exception.GetType(), out var translator)
            && translator.Translate(exception) is { } translated)
        {
            return translated;
        }

        return OutageClassifier.IsOutage(exception) ? ExceptionProblem.Outage : null;
    }
}
