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
/// included: <see cref="HttpClient.Timeout"/> (and any other deadline) surfaces as an
/// <see cref="OperationCanceledException"/> while the caller's token is still live. Only the
/// caller's own cancellation propagates.
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

        if (breachCount < _settings.RejectThreshold)
        {
            return Result.Success;
        }

        if (_settings.Mode == BreachAction.Warn)
        {
            _warningContext.Add(new PasswordWarning("User.PasswordBreached", BreachWarningMessage));
            _logger.LogInformation(
                "Breached password accepted with warning (Warn mode); breach count {BreachCount}.", breachCount);
            return Result.Success;
        }

        _logger.LogInformation(
            "Breached password rejected (Enforce mode); breach count {BreachCount}.", breachCount);
        return UserErrors.PasswordBreached;
    }
}
