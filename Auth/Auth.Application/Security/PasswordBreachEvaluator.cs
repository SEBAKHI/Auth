using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Security;

/// <summary>
/// Applies the configured breached-password policy. Single shared decision point for the
/// Register / ChangePassword / ResetPassword / CreateUser handlers.
/// </summary>
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
        IOptions<PasswordSettings> passwordSettings,
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Availability failure: don't let an external dependency block password changes by default.
            if (_settings.FailOpen)
            {
                _logger.LogWarning(ex, "Breached-password check failed; allowing password (fail-open).");
                return Result.Success;
            }

            _logger.LogError(ex, "Breached-password check failed; rejecting password (fail-closed).");
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
