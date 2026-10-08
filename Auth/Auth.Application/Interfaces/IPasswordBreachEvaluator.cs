using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Applies the configured breached-password policy to a candidate password.
/// Encapsulates the enabled flag, the Enforce/Warn mode, the reject threshold, and fail-open
/// behaviour so the five password-setting handlers (CompleteRegistration, RegisterWithInvitation,
/// ChangePassword, ResetPassword, CreateUser) can share a single call site.
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
    /// <see cref="IPasswordWarningContext"/>. "Unavailable" covers every failure of the check the
    /// caller did not cause, a timeout included.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled <paramref name="cancellationToken"/>; the caller's own cancellation is
    /// never turned into a fail-open success or a fail-closed error.
    /// </exception>
    Task<ErrorOr<Success>> EvaluateAsync(string password, CancellationToken cancellationToken);
}
