using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Security;

/// <summary>
/// Applies the configured breached-password policy. Single shared decision point for the five
/// password-setting handlers: CompleteRegistration, RegisterWithInvitation, ChangePassword,
/// ResetPassword and CreateUser.
/// </summary>
/// <remarks>
/// Any failure of the check that the caller did not cause follows <c>FailOpen</c>, a timeout
/// included: the checker's per-call deadline (and <see cref="HttpClient.Timeout"/> behind it)
/// surfaces as an <see cref="OperationCanceledException"/> while the caller's token is still live.
/// Only the caller's own cancellation propagates. The settings are read once per DI scope
/// (<see cref="IOptionsSnapshot{TOptions}"/>), so a value saved in the console, Enabled included,
/// applies from the next request.
/// </remarks>
public sealed class PasswordBreachEvaluator : IPasswordBreachEvaluator
{
    private readonly IBreachedPasswordChecker _checker;
    private readonly IPasswordWarningContext _warningContext;
    private readonly BreachedPasswordCheckSettings _settings;
    private readonly ILogger<PasswordBreachEvaluator> _logger;

    private const string BreachWarningMessage =
        "This password has appeared in a known data breach. For your security, consider choosing a different one.";

    public PasswordBreachEvaluator(
        IBreachedPasswordChecker checker,
        IPasswordWarningContext warningContext,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<PasswordBreachEvaluator> logger)
    {
        _checker = checker;
        _warningContext = warningContext;
        _settings = passwordSettings.Value.BreachedPasswordCheck;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> EvaluateAsync(string password, CancellationToken cancellationToken)
    {
        // Disabled => fully inert (no external call).
        if (!_settings.Enabled || string.IsNullOrEmpty(password))
        {
            return Result.Success;
        }

        int breachCount;
        try
        {
            breachCount = await _checker.GetBreachCountAsync(password, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The caller may have gone away while a transport failure was in flight. Its
            // cancellation wins over that failure: no fail-open success for an abandoned request,
            // and no warning about a check nobody is waiting for.
            cancellationToken.ThrowIfCancellationRequested();

            // Availability failure: don't let an external dependency block password changes by default.
            // A cancellation the caller did not request is a deadline (HttpClient.Timeout or any other),
            // judged by the caller's token rather than the inner exception, which a deadline may not carry.
            var failureKind = ex is OperationCanceledException ? "Timeout" : "Error";

            if (_settings.FailOpen)
            {
                _logger.LogWarning(
                    ex, "Breached-password check failed ({FailureKind}); allowing password (fail-open).", failureKind);
                return Result.Success;
            }

            _logger.LogError(
                ex, "Breached-password check failed ({FailureKind}); rejecting password (fail-closed).", failureKind);
            return UserErrors.PasswordBreachCheckUnavailable;
        }

        // Clamped like the checker's TimeoutMs: the console bounds the value, but a file or a
        // direct write does not, and a threshold of 0 would refuse every new password.
        var rejectThreshold = Math.Clamp(
            _settings.RejectThreshold,
            BreachedPasswordCheckSettings.MinRejectThreshold,
            BreachedPasswordCheckSettings.MaxRejectThreshold);
        if (breachCount < rejectThreshold)
        {
            return Result.Success;
        }

        // No line carries the breach count: a high count all but names the password in HIBP's
        // public corpus, and these lines are written inside the request's log scope.
        if (_settings.Mode == BreachAction.Warn)
        {
            _warningContext.Add(new PasswordWarning("User.PasswordBreached", BreachWarningMessage));
            _logger.LogInformation("Breached password accepted with warning (Warn mode).");
            return Result.Success;
        }

        _logger.LogInformation("Breached password rejected (Enforce mode).");
        return UserErrors.PasswordBreached;
    }
}
