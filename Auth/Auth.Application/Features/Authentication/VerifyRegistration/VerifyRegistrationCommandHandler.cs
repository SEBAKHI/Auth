using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyRegistration;

/// <summary>
/// Checks a registration code without consuming it.
/// </summary>
/// <remarks>
/// <para>
/// There is no branch here on anything but the code. No door check — the
/// server's registration policy was applied when the row was created and will
/// be applied again at completion; refusing a code on a closed server would
/// only tell the caller something about the server. No address, no user
/// lookup: the handle names the row, and a row minted for a taken address
/// carries a code that was never sent, so a guess against it fails exactly as
/// a guess against a free address's code does.
/// </para>
/// <para>
/// The attempt gate and the comparison run under the row's lock inside the
/// repository, in one transaction, so a burst of concurrent guesses at one
/// row is serialized and the five-attempt limit holds whatever the quota. The
/// counter is shared with the completion step, which checks the same code the
/// same way before it consumes.
/// </para>
/// </remarks>
public class VerifyRegistrationCommandHandler : IRequestHandler<VerifyRegistrationCommand, ErrorOr<Success>>
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly ILogger<VerifyRegistrationCommandHandler> _logger;

    public VerifyRegistrationCommandHandler(
        IPendingRegistrationRepository pendingRegistrations,
        ILogger<VerifyRegistrationCommandHandler> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(VerifyRegistrationCommand request, CancellationToken cancellationToken)
    {
        // The validator refuses this first; kept here so the handler is safe
        // on its own, and so a malformed code never reaches the lock.
        if (string.IsNullOrWhiteSpace(request.Otp) ||
            request.Otp.Length != 6 ||
            !request.Otp.All(char.IsDigit))
        {
            return EmailVerificationErrors.InvalidOtpFormat;
        }

        var check = await _pendingRegistrations.CheckCodeUnderLockAsync(request.PendingId, request.Otp, cancellationToken);

        switch (check.Outcome)
        {
            case PendingRegistrationCodeOutcome.Match:
                return Result.Success;

            case PendingRegistrationCodeOutcome.Exhausted:
                // The gate refused before any comparison: no attempt was charged
                // and no hash was computed. Named separately so the screen can
                // offer a new code instead of another guess.
                _logger.LogWarning("Registration code for pending row {PendingRegistrationId} is exhausted", check.Row!.Id);
                return EmailVerificationErrors.TooManyAttempts;

            case PendingRegistrationCodeOutcome.Wrong:
                _logger.LogWarning(
                    "Wrong registration code for pending row {PendingRegistrationId}; attempts so far {AttemptCount}",
                    check.Row!.Id, check.Row.AttemptCount);
                return EmailVerificationErrors.InvalidOrExpiredOtp;

            default:
                // No live row under this handle: unknown, expired, or consumed.
                // One answer for all three — telling them apart is the
                // enumeration the handle exists to prevent.
                return EmailVerificationErrors.InvalidOrExpiredOtp;
        }
    }
}
