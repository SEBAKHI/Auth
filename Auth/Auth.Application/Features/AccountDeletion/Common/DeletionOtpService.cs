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
/// Issues and verifies deletion re-authentication OTPs — the single factor
/// behind both the in-app flow and the public no-login flow. Mirrors the email
/// verification semantics: Argon2id-hashed 6-digit code, short expiry, capped
/// attempts, issuance rate limiting. Every verification failure shape maps to
/// the single generic InvalidOtp error (anti-enumeration).
/// </summary>
public class DeletionOtpService
{
    /// <summary>
    /// Outstanding codes examined per verification. Bounds the Argon2id work a
    /// single request can provoke while staying well above what a legitimate
    /// user can hold at once (issuance is rate limited per address).
    /// </summary>
    private const int MaxVerificationCandidates = 5;


    private readonly IAccountDeletionVerificationRepository _verificationRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AccountDeletionSettings _settings;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<DeletionOtpService> _logger;

    public DeletionOtpService(
        IAccountDeletionVerificationRepository verificationRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        IOptionsSnapshot<AccountDeletionSettings> settings,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<DeletionOtpService> logger)
    {
        _verificationRepository = verificationRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _settings = settings.Value;
        _emailSettings = emailSettings.Value;
        _environment = environment;
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

        // With email disabled the log is the only other place the code exists,
        // which is what makes the flow testable locally. Gated on the environment
        // as well as the setting: this code IS the credential - it is the single
        // factor behind an irreversible deletion, and issuance is reachable
        // anonymously through the public wizard, so anyone who knows an address
        // can make this line fire. Email:Enabled is a hot setting an operator can
        // flip from the console in production, so on its own it would put a live
        // deletion code in the production log. Note the code is not masked the way
        // the address beside it is.
        if (!_emailSettings.Enabled && _environment.IsDevelopment)
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
    /// Verifies a deletion code for an already-identified user and consumes it.
    /// Adds the owner binding an address lookup alone cannot give: a code is
    /// only ever valid for the account it was issued to, so a row left behind
    /// by a hard-deleted account whose address was later re-registered can
    /// never confirm the new owner's deletion.
    /// </summary>
    public async Task<ErrorOr<AccountDeletionVerification>> VerifyForUserAsync(
        User user, string otpCode, CancellationToken cancellationToken)
    {
        var result = await VerifyAsync(user.Email, otpCode, cancellationToken);
        if (result.IsError)
        {
            return result.Errors;
        }

        if (result.Value.UserId != user.Id)
        {
            _logger.LogWarning(
                "Deletion code for {Email} is bound to a different account; refused",
                EmailMasking.Mask(user.Email));
            return AccountDeletionErrors.InvalidOtp;
        }

        return result;
    }

    /// <summary>
    /// Verifies a deletion code for an email address and consumes it. Unknown
    /// email, wrong code, expired code and exhausted attempts are all the same
    /// generic error.
    /// </summary>
    public async Task<ErrorOr<AccountDeletionVerification>> VerifyAsync(
        string email, string otpCode, CancellationToken cancellationToken)
    {
        // Every outstanding code is redeemable, not just the newest: issuance is
        // reachable anonymously (the public wizard), so matching the newest row
        // alone would let anyone who knows an address orphan the code its owner
        // is holding, simply by asking for another one.
        var candidates = await _verificationRepository.GetValidForEmailAsync(
            email, MaxVerificationCandidates, cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!candidate.IsValid || !_passwordHasher.VerifyPassword(otpCode, candidate.OtpHash))
            {
                continue;
            }

            var used = candidate.MarkAsUsed();
            if (used.IsError)
            {
                return used.Errors;
            }

            await _verificationRepository.MarkAsUsedAsync(candidate.Id, cancellationToken);
            return candidate;
        }

        // Nothing matched. The failed attempt is charged to the newest code
        // only — charging every candidate would let one caller's wrong guesses
        // exhaust the attempt budget of a code someone else is holding.
        var newest = candidates.FirstOrDefault(candidate => candidate.IsValid);
        if (newest is not null)
        {
            await _verificationRepository.IncrementAttemptCountAsync(newest.Id, cancellationToken);
        }

        return AccountDeletionErrors.InvalidOtp;
    }
}
