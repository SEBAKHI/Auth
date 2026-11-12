using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs the bot check (S13, BD3) on every request marked
/// <see cref="IBotProtectedRequest"/>, after validation and before the handler: so before any
/// database read and before the password hash. Every other request passes through untouched.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Off</c>: nothing is read or called.</item>
/// <item><c>Monitor</c>: the token is verified and the verdict recorded; nothing is refused.</item>
/// <item><c>Enforce</c>: no token is <c>BotDefense.TokenMissing</c>, a refused one
/// <c>BotDefense.TokenRejected</c> (both 400); a check that cannot run follows the fail mode
/// (<see cref="BotDefenseFailurePolicy"/>). With neither key set (Unconfigured) the request goes
/// on: there is no check in place, which the posture reports.</item>
/// </list>
/// Refusals are returned as ErrorOr errors, as <see cref="ValidationBehavior{TRequest,TResponse}"/>
/// returns its own; nothing is thrown. There is no bypass: no header, account or address list
/// skips the check.
/// </remarks>
public sealed class BotDefenseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    private readonly IOptionsMonitor<BotDefenseSettings> _settings;
    private readonly IBotDefenseVerifier _verifier;
    private readonly BotDefenseFailurePolicy _failurePolicy;
    private readonly IBotDefenseVerdictRecorder _recorder;

    public BotDefenseBehavior(
        IOptionsMonitor<BotDefenseSettings> settings,
        IBotDefenseVerifier verifier,
        BotDefenseFailurePolicy failurePolicy,
        IBotDefenseVerdictRecorder recorder)
    {
        _settings = settings;
        _verifier = verifier;
        _failurePolicy = failurePolicy;
        _recorder = recorder;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IBotProtectedRequest protectedRequest)
        {
            return await next();
        }

        var settings = _settings.CurrentValue;
        if (settings.Mode == BotDefenseMode.Off)
        {
            return await next();
        }

        var verdict = await _verifier.VerifyAsync(protectedRequest.BotToken, protectedRequest.Action, cancellationToken);
        _recorder.Record(protectedRequest.Action, settings.Mode, verdict);

        if (settings.Mode == BotDefenseMode.Monitor
            || verdict.Kind is BotDefenseVerdictKind.Passed or BotDefenseVerdictKind.Unconfigured)
        {
            return await next();
        }

        Error? refusal = verdict.Kind switch
        {
            BotDefenseVerdictKind.Missing => BotDefenseErrors.TokenMissing,
            BotDefenseVerdictKind.Rejected => BotDefenseErrors.TokenRejected,
            _ => null,
        };

        if (refusal is { } error)
        {
            return (dynamic)new List<Error> { error };
        }

        // Unavailable or Misconfigured: the check could not judge this request.
        var decision = _failurePolicy.Decide(settings.FailMode, protectedRequest.Action, protectedRequest.ClientIp);
        if (decision.IsError)
        {
            return (dynamic)decision.Errors;
        }

        return await next();
    }
}
