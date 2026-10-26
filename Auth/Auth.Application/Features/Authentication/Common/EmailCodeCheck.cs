using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The code check every step of a sign-in by emailed code starts with (OI-215): email
/// sending, the code's shape, then the code under the pending row's lock, which
/// charges an attempt when it is wrong. Five wrong codes across every step close the
/// code, as for the registration, and the address's wrong codes count against its
/// 24-hour ceiling (D-110-2). Nothing is consumed and nothing about the address is read.
/// </summary>
/// <remarks>
/// With email sending off no code of this door was ever delivered to anyone: the start
/// refuses, and a code minted by the registration's start was never sent to an address
/// that has an account. So the three steps that take a code — verify, sign-in and the
/// recovery — refuse before the lookup, with the start's own refusal, the same for every
/// handle: no row is read and no attempt is charged.
/// </remarks>
public class EmailCodeCheck
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly IOptionsSnapshot<EmailSettings> _emailSettings;
    private readonly ILogger<EmailCodeCheck> _logger;

    public EmailCodeCheck(
        IPendingRegistrationRepository pendingRegistrations,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<EmailCodeCheck> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _emailSettings = emailSettings;
        _logger = logger;
    }

    /// <summary>Checks <paramref name="otp"/> against the live row behind <paramref name="pendingId"/>.</summary>
    /// <returns>
    /// The row the code matched; or <c>User.EmailCodeSignInUnavailable</c> while email
    /// sending is off, <c>EmailVerification.InvalidOtpFormat</c>,
    /// <c>EmailVerification.WrongCodeLimitReached</c>, <c>EmailVerification.TooManyAttempts</c>,
    /// or <c>EmailVerification.InvalidOrExpiredOtp</c> for an unknown, expired or consumed
    /// handle and for a wrong code alike.
    /// </returns>
    public async Task<ErrorOr<PendingRegistration>> CheckAsync(
        string pendingId, string otp, CancellationToken cancellationToken)
    {
        if (!_emailSettings.Value.Enabled)
        {
            _logger.LogInformation("A code of the sign-in by emailed code was refused: email sending is off");
            return UserErrors.EmailCodeSignInUnavailable;
        }

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

            case PendingRegistrationCodeOutcome.WrongCodeCeilingReached:
                _logger.LogWarning(
                    "Sign-in code for pending row {PendingRegistrationId} refused: the address reached its wrong-code ceiling",
                    check.Row!.Id);
                return EmailVerificationErrors.WrongCodeLimitReached;

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
