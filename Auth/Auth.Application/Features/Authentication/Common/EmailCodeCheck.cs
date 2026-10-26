using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The code check every step of a sign-in by emailed code starts with (OI-215): the
/// code's shape, then the code under the pending row's lock, which charges an attempt
/// when it is wrong. Five wrong codes across every step close the code, as for the
/// registration. Nothing is consumed and nothing about the address is read.
/// </summary>
public class EmailCodeCheck
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly ILogger<EmailCodeCheck> _logger;

    public EmailCodeCheck(IPendingRegistrationRepository pendingRegistrations, ILogger<EmailCodeCheck> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _logger = logger;
    }

    /// <summary>Checks <paramref name="otp"/> against the live row behind <paramref name="pendingId"/>.</summary>
    /// <returns>
    /// The row the code matched; or <c>EmailVerification.InvalidOtpFormat</c>,
    /// <c>EmailVerification.TooManyAttempts</c>, or <c>EmailVerification.InvalidOrExpiredOtp</c>
    /// for an unknown, expired or consumed handle and for a wrong code alike.
    /// </returns>
    public async Task<ErrorOr<PendingRegistration>> CheckAsync(
        string pendingId, string otp, CancellationToken cancellationToken)
    {
        // The validator refuses this first; kept so a malformed code never reaches
        // the lock whoever calls.
        if (string.IsNullOrWhiteSpace(otp) || otp.Length != 6 || !otp.All(char.IsDigit))
        {
            return EmailVerificationErrors.InvalidOtpFormat;
        }

        var check = await _pendingRegistrations.CheckCodeUnderLockAsync(pendingId, otp, cancellationToken);

        switch (check.Outcome)
        {
            case PendingRegistrationCodeOutcome.Match:
                return check.Row!;

            case PendingRegistrationCodeOutcome.Exhausted:
                _logger.LogWarning("Sign-in code for pending row {PendingRegistrationId} is exhausted", check.Row!.Id);
                return EmailVerificationErrors.TooManyAttempts;

            case PendingRegistrationCodeOutcome.Wrong:
                _logger.LogWarning(
                    "Wrong sign-in code for pending row {PendingRegistrationId}; attempts so far {AttemptCount}",
                    check.Row!.Id, check.Row.AttemptCount);
                return EmailVerificationErrors.InvalidOrExpiredOtp;

            default:
                // Unknown, expired or consumed: one answer for all three.
                return EmailVerificationErrors.InvalidOrExpiredOtp;
        }
    }
}
