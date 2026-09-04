using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ResendEmailVerification;

/// <summary>
/// Handler for resending email verification OTP.
/// </summary>
public class ResendEmailVerificationCommandHandler
    : IRequestHandler<ResendEmailVerificationCommand, ErrorOr<ResendEmailVerificationResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOtpHasher _otpHasher;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<ResendEmailVerificationCommandHandler> _logger;

    public ResendEmailVerificationCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IOtpHasher otpHasher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<ResendEmailVerificationCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _otpHasher = otpHasher;
        _emailSettings = emailSettings.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ErrorOr<ResendEmailVerificationResponse>> Handle(
        ResendEmailVerificationCommand request,
        CancellationToken cancellationToken)
    {
        // Get the user by email
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // For security, always return success-like response to prevent email enumeration
        // But only actually send email if user exists and is not verified
        if (user == null)
        {
            _logger.LogWarning(
                "Resend verification attempted for non-existent email: {Email}",
                EmailMasking.Mask(request.Email));

            // Return fake response to prevent enumeration
            return new ResendEmailVerificationResponse(
                DateTime.UtcNow.AddMinutes(_emailSettings.OtpExpirationMinutes),
                EmailMasking.Mask(request.Email));
        }

        // Check if already verified
        if (user.EmailConfirmed)
        {
            return EmailVerificationErrors.EmailAlreadyVerified;
        }

        // One live code per account. A valid code (unused, unexpired, attempts
        // remaining) is still in the owner's inbox, so nothing is reissued — no
        // new code, no invalidation, no mail — and the reply is the same generic
        // shape as the unknown-address branch above. Before this check every
        // resend invalidated the existing code before minting the next, so a
        // stranger calling this endpoint with someone else's address killed the
        // code that person was about to type. A code whose attempts are
        // exhausted is not live: its owner must be able to get a fresh one.
        var liveCode = await _tokenRepository.GetValidTokenForUserAsync(user.Id, cancellationToken);
        if (liveCode is { IsValid: true })
        {
            _logger.LogInformation(
                "Verification code requested for user {UserId} while a live code exists; not reissued",
                user.Id);

            return new ResendEmailVerificationResponse(
                DateTime.UtcNow.AddMinutes(_emailSettings.OtpExpirationMinutes),
                EmailMasking.Mask(user.Email));
        }

        // Rate limiting check
        var recentCount = await _tokenRepository.GetRecentTokenCountAsync(
            user.Email,
            _emailSettings.RateLimitWindow,
            cancellationToken);

        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for email verification resend: {Email}",
                EmailMasking.Mask(user.Email));
            return EmailVerificationErrors.TooManyRequests;
        }

        // Only dead codes (expired, used, or attempts exhausted) remain here;
        // marking them used is hygiene, and it no longer touches a code the
        // owner is still holding.
        await _tokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate OTP
        var otp = _otpGenerator.GenerateNumericOtp(6);
        var otpHash = _otpHasher.Hash(user.Id.ToString(), otp);

        // With email disabled the log is the only other place the OTP exists,
        // which is what makes the flow testable locally. Gated on the environment
        // as well as the setting: this code is a bearer credential - presenting it
        // confirms ownership of the address with no further proof - and
        // Email:Enabled is a hot setting an operator can flip from the console in
        // production, so on its own it would put a live verification code in the
        // production log. Note the code is written unmasked, unlike the address
        // beside it.
        if (!_emailSettings.Enabled && _environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Email disabled - OTP for {Email}: {Otp} (expires in {Minutes} minutes)",
                EmailMasking.Mask(user.Email), otp, _emailSettings.OtpExpirationMinutes);
        }

        // Create token
        var token = EmailVerificationToken.Create(
            user.Id,
            otpHash,
            user.Email,
            _emailSettings.OtpExpirationMinutes);

        await _tokenRepository.CreateAsync(token, cancellationToken);

        // Send email from the database-managed template; language follows the
        // user's stored PreferredLanguage.
        var recipientName = user.DisplayName ?? user.FirstName ?? "User";
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.EmailVerification,
                RecipientAddress = user.Email,
                RecipientName = recipientName,
                RecipientUserId = user.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["OtpCode"] = otp,
                    ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to resend verification email to {UserId}: {Error}",
                user.Id, sendResult.FirstError.Description);
            return EmailVerificationErrors.EmailSendFailed;
        }

        _logger.LogInformation(
            "Verification OTP resent to user {UserId} ({Email})",
            user.Id, EmailMasking.Mask(user.Email));

        return new ResendEmailVerificationResponse(token.ExpiresAt, EmailMasking.Mask(user.Email));
    }
}
