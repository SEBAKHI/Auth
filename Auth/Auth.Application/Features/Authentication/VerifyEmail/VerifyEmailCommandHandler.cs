using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Handler for verifying email using OTP. On the anonymous (email-keyed) path it
/// also signs the user in — the OTP proves control of the address, so the
/// just-registered (or just-authenticated) user is handed a session directly
/// instead of being bounced to a manual sign-in. The admin (user-id-keyed) path
/// only confirms the address and issues no tokens.
/// </summary>
public class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, ErrorOr<VerifyEmailResult>>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly IOtpHasher _otpHasher;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IAccountLockout _accountLockout;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        IOtpHasher otpHasher,
        ILoginResponseBuilder loginResponseBuilder,
        ITwoFactorChallengeService twoFactorChallengeService,
        IDomainEventDispatcher eventDispatcher,
        IAccountLockout accountLockout,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _otpHasher = otpHasher;
        _loginResponseBuilder = loginResponseBuilder;
        _twoFactorChallengeService = twoFactorChallengeService;
        _eventDispatcher = eventDispatcher;
        _accountLockout = accountLockout;
        _logger = logger;
    }

    public async Task<ErrorOr<VerifyEmailResult>> Handle(
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        // Validate OTP format
        if (string.IsNullOrWhiteSpace(request.Otp) ||
            request.Otp.Length != 6 ||
            !request.Otp.All(char.IsDigit))
        {
            return EmailVerificationErrors.InvalidOtpFormat;
        }

        // Get the user by ID (admin flows) or by email (anonymous flows).
        User? user;
        if (request.UserId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(request.UserId.Value, cancellationToken);
            if (user == null)
            {
                return EmailVerificationErrors.UserNotFound;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return EmailVerificationErrors.UserNotFound;
            }

            user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
            if (user == null)
            {
                // Do not reveal account existence on the anonymous email-keyed path.
                return EmailVerificationErrors.InvalidOrExpiredOtp;
            }
        }

        // Already verified. On the admin path that is a real answer. On the
        // anonymous, email-keyed path it is the same answer an unknown address
        // gets: this endpoint is reachable twenty times a minute per client,
        // and "already verified" for every confirmed account was a
        // registered-or-not oracle — the enumeration channel the verify-first
        // registration flow closes everywhere else. Known residual, left as
        // is: an UNCONFIRMED account with a live code still answers
        // TooManyAttempts after five wrong codes where an unknown address
        // keeps answering InvalidOrExpiredOtp; that path serves admin-created
        // accounts and is not this flow's to reshape.
        if (user.EmailConfirmed)
        {
            return request.UserId.HasValue
                ? EmailVerificationErrors.EmailAlreadyVerified
                : EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // Get valid token for user
        var token = await _tokenRepository.GetValidTokenForUserAsync(user.Id, cancellationToken);
        if (token == null)
        {
            _logger.LogWarning(
                "No valid verification token found for user {UserId}",
                user.Id);
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // Check attempt count. Kept as the fast path, so a sequence of wrong codes
        // still ends in TooManyAttempts exactly as before; the reservation below
        // is what holds the cap when requests arrive together.
        if (token.AttemptCount >= EmailVerificationToken.MaxAttempts)
        {
            _logger.LogWarning(
                "Max verification attempts exceeded for user {UserId}",
                user.Id);
            return EmailVerificationErrors.TooManyAttempts;
        }

        // Reserve the attempt before the code is checked. A burst of concurrent
        // guesses all read the same count above; only this conditional statement
        // stops the sixth of them. Refused means spent, expired, superseded by a
        // newer code, or lost to a concurrent request: one answer for all four.
        var attempt = await _tokenRepository.TryReserveAttemptAsync(
            token.Id, EmailVerificationToken.MaxAttempts, cancellationToken);
        if (attempt == null)
        {
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // Verify OTP using Argon2id
        var isValid = _otpHasher.Verify(user.Id.ToString(), request.Otp, token.OtpHash);

        if (!isValid)
        {
            // The reserved attempt stays counted.
            var remainingAttempts = EmailVerificationToken.MaxAttempts - attempt.Value;
            _logger.LogWarning(
                "Invalid OTP for user {UserId}. Remaining attempts: {RemainingAttempts}",
                user.Id, remainingAttempts);

            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // OTP is valid - consume the token, then confirm the email. Two correct
        // codes arriving together both reach this line, and only the one that
        // consumes the token may confirm the address and sign the user in.
        if (!await _tokenRepository.TryConsumeAsync(token.Id, cancellationToken))
        {
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        await _userRepository.ConfirmEmailAsync(user.Id, user.Id, cancellationToken);

        _logger.LogInformation(
            "Email verified successfully for user {UserId} ({Email})",
            user.Id, EmailMasking.Mask(user.Email));

        // Admin (user-id-keyed) path: confirm only, never issue tokens for another
        // user. The controller maps a null login to 204 No Content.
        if (request.UserId.HasValue)
        {
            return new VerifyEmailResult(null);
        }

        // Anonymous (email-keyed) self-service path: sign the user in. This mirrors
        // the login handler's tail so the issued session is identical to a normal
        // login (roles/permissions, refresh token, session row, audit trail).
        return await IssueLoginAsync(user, request, cancellationToken);
    }

    /// <summary>
    /// Completes sign-in for the self-service path after the address is confirmed.
    /// Re-checks account status and honours two-factor, then delegates token
    /// issuance to the shared login response builder.
    /// </summary>
    private async Task<ErrorOr<VerifyEmailResult>> IssueLoginAsync(
        User user,
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        // Lockout, by the one classifier. The code just entered proves control of
        // the mailbox, so the courtesy sign-in is ADMITTED under a lock raised by
        // strangers' wrong passwords (timed or hard), as it always was; but it
        // proves nothing about the password, so it no longer LIFTS that lock
        // (owner decision G8): the next password sign-in still meets it, and a
        // reset is still the way out of a hard one. An administrator's lock is
        // honoured: the address is verified and stays verified, only the sign-in
        // is refused. A lapsed lock is repaired as the sign-in handlers do.
        var lockKind = user.LockKind(DateTime.UtcNow);
        if (lockKind == AccountLockKind.Admin)
        {
            return user.LockRefusal(lockKind);
        }

        if (lockKind == AccountLockKind.Lapsed)
        {
            await _accountLockout.RepairLapsedLockAsync(user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }

        // Defensive parity with login: a user cannot enable two-factor before
        // confirming their email, but if that ever holds we must not skip the
        // challenge — hand back a pending-2FA response instead of tokens.
        if (user.TwoFactorEnabled)
        {
            var challengeToken = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, request.UserAgent, AuthenticationMethods.EmailCode, cancellationToken);

            return new VerifyEmailResult(new LoginResponse
            {
                RequiresTwoFactor = true,
                TwoFactorChallengeToken = challengeToken
            });
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, AuthenticationMethods.EmailCode,
            passwordProven: false, cancellationToken);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit. The email is verified and stays
            // verified — only the courtesy auto sign-in is refused, and the
            // pending UserLoggedInEvent is dropped with it.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        _logger.LogInformation(
            "Auto sign-in completed after email verification for user {UserId}",
            user.Id);

        return new VerifyEmailResult(loginResponse.Value);
    }
}
