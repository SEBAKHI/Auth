using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
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
    private readonly Auth.Application.Configuration.PasswordSettings _passwordSettings;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        IOtpHasher otpHasher,
        ILoginResponseBuilder loginResponseBuilder,
        ITwoFactorChallengeService twoFactorChallengeService,
        IDomainEventDispatcher eventDispatcher,
        Microsoft.Extensions.Options.IOptionsSnapshot<Auth.Application.Configuration.PasswordSettings> passwordSettings,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _otpHasher = otpHasher;
        _loginResponseBuilder = loginResponseBuilder;
        _twoFactorChallengeService = twoFactorChallengeService;
        _eventDispatcher = eventDispatcher;
        _passwordSettings = passwordSettings.Value;
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

        // Check if already verified
        if (user.EmailConfirmed)
        {
            return EmailVerificationErrors.EmailAlreadyVerified;
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

        // Check attempt count
        if (token.AttemptCount >= EmailVerificationToken.MaxAttempts)
        {
            _logger.LogWarning(
                "Max verification attempts exceeded for user {UserId}",
                user.Id);
            return EmailVerificationErrors.TooManyAttempts;
        }

        // Verify OTP using Argon2id
        var isValid = _otpHasher.Verify(user.Id.ToString(), request.Otp, token.OtpHash);

        if (!isValid)
        {
            // Increment attempt count
            await _tokenRepository.IncrementAttemptCountAsync(token.Id, cancellationToken);

            var remainingAttempts = EmailVerificationToken.MaxAttempts - token.AttemptCount - 1;
            _logger.LogWarning(
                "Invalid OTP for user {UserId}. Remaining attempts: {RemainingAttempts}",
                user.Id, remainingAttempts);

            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // OTP is valid - mark token as used and confirm email
        await _tokenRepository.MarkAsUsedAsync(token.Id, cancellationToken);
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

        // Lockout, mirroring the sign-in handlers. The code just entered proves
        // control of the mailbox exactly as a completed password reset does, so
        // a lock raised by strangers' wrong passwords is cleared before the
        // courtesy sign-in; an administrator's lock is honoured — the address is
        // verified and stays verified, only the sign-in is refused. Without this
        // the sign-in below reached RecordSuccessfulLoginAsync on a Locked row
        // and left it Locked with no expiry: an indefinite lock that credential
        // renewal refuses and no self-service path could clear.
        if (user.IsLockedOut())
        {
            if (!user.IsLockedByFailedAttempts(_passwordSettings.MaxFailedAttempts))
            {
                return UserErrors.AccountLockedUntil(user.LockoutEnd);
            }

            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }
        else if (user.Status == Auth.Domain.Enums.UserStatus.Locked)
        {
            // Expired lock left on the row: repair it as the sign-in handlers do.
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }

        // Defensive parity with login: a user cannot enable two-factor before
        // confirming their email, but if that ever holds we must not skip the
        // challenge — hand back a pending-2FA response instead of tokens.
        if (user.TwoFactorEnabled)
        {
            var challengeToken = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, request.UserAgent, cancellationToken);

            return new VerifyEmailResult(new LoginResponse
            {
                RequiresTwoFactor = true,
                TwoFactorChallengeToken = challengeToken
            });
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken);

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
