using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Applies the configured breached-password policy to a candidate password.
/// Encapsulates the enabled flag, the Enforce/Warn mode, the reject threshold, and fail-open
/// behaviour so the four password-setting handlers can share a single call site.
/// </summary>
public interface IPasswordBreachEvaluator
{
    /// <summary>
    /// Evaluates a candidate password against the breach corpus.
    /// </summary>
    /// <returns>
    /// An error when the password is breached and the policy is <c>Enforce</c> (or the service is
    /// unavailable and configured to fail closed); otherwise <see cref="Success"/>. In <c>Warn</c>
    /// mode a breached password yields success after recording a warning via
    /// <see cref="IPasswordWarningContext"/>.
    /// </returns>
    Task<ErrorOr<Success>> EvaluateAsync(string password, CancellationToken cancellationToken);
}
