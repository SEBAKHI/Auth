using System.Diagnostics;
using ErrorOr;
using MediatR;

namespace Auth.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs request execution details including type, duration, and outcome.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (response.IsError)
        {
            _logger.LogWarning(
                "Completed {RequestName} with errors in {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "Completed {RequestName} in {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }

        if (stopwatch.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "Long-running request: {RequestName} took {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }

        return response;
    }
}
