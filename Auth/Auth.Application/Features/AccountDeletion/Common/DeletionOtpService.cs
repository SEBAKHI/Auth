using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.AccountDeletion.Common;

/// <summary>
/// Issues and verifies deletion re-authentication OTPs — shared by the
/// passwordless in-app flow and the public no-login flow. Mirrors the email
/// verification semantics: Argon2id-hashed 6-digit code, short expiry, capped
/// attempts, issuance rate limiting. Every verification failure shape maps to
/// the single generic InvalidOtp error (anti-enumeration).
/// </summary>
public class DeletionOtpService
{
    private readonly IAccountDeletionVerificationRepository _verificationRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AccountDeletionSettings _settings;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<DeletionOtpService> _logger;

    public DeletionOtpService(
        IAccountDeletionVerificationRepository verificationRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        IOptionsSnapshot<AccountDeletionSettings> settings,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<DeletionOtpService> logger)
    {
        _verificationRepository = verificationRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _settings = settings.Value;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates, stores and emails a deletion verification code for the user.
    /// </summary>
    public async Task<ErrorOr<Success>> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var recentCount = await _verificationRepository.GetRecentCountAsync(
            user.Email, _emailSettings.RateLimitWindow, cancellationToken);
        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for deletion verification: {Email}", EmailMasking.Mask(user.Email));
            return AccountDeletionErrors.TooManyRequests;
        }

        var otp = _otpGenerator.GenerateNumericOtp(6);

        // Log OTP when email is disabled (development mode)
        if (!_emailSettings.Enabled)
        {
            _logger.LogWarning(
                "Email disabled - deletion OTP for {Email}: {Otp} (expires in {Minutes} minutes)",
                EmailMasking.Mask(user.Email), otp, _settings.OtpExpirationMinutes);
        }

        var verification = AccountDeletionVerification.Create(
            user.Id, user.Email, _passwordHasher.HashPassword(otp), _settings.OtpExpirationMinutes);
        await _verificationRepository.CreateAsync(verification, cancellationToken);

        var recipientName = AccountDeletionRequestor.DisplayNameOf(user);
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.AccountDeletionVerification,
                RecipientAddress = user.Email,
                RecipientName = recipientName,
                RecipientUserId = user.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["OtpCode"] = otp,
                    ["ExpirationMinutes"] = _settings.OtpExpirationMinutes
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send deletion verification email to {UserId}: {Error}",
                user.Id, sendResult.FirstError.Description);
            return EmailVerificationErrors.EmailSendFailed;
        }

        return Result.Success;
    }

    /// <summary>
    /// Verifies a deletion code for an email address and consumes it. Unknown
    /// email, wrong code, expired code and exhausted attempts are all the same
    /// generic error.
    /// </summary>
    public async Task<ErrorOr<AccountDeletionVerification>> VerifyAsync(
        string email, string otpCode, CancellationToken cancellationToken)
    {
        var verification = await _verificationRepository.GetValidForEmailAsync(email, cancellationToken);
        if (verification is null || !verification.IsValid)
        {
            return AccountDeletionErrors.InvalidOtp;
        }

        if (!_passwordHasher.VerifyPassword(otpCode, verification.OtpHash))
        {
            await _verificationRepository.IncrementAttemptCountAsync(verification.Id, cancellationToken);
            return AccountDeletionErrors.InvalidOtp;
        }

        var used = verification.MarkAsUsed();
        if (used.IsError)
        {
            return used.Errors;
        }

        await _verificationRepository.MarkAsUsedAsync(verification.Id, cancellationToken);
        return verification;
    }
}
