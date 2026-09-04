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

namespace Auth.Application.Features.Authentication.SendEmailVerification;

/// <summary>
/// Handler for sending email verification OTP.
/// </summary>
public class SendEmailVerificationCommandHandler
    : IRequestHandler<SendEmailVerificationCommand, ErrorOr<SendEmailVerificationResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOtpHasher _otpHasher;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<SendEmailVerificationCommandHandler> _logger;

    public SendEmailVerificationCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IOtpHasher otpHasher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<SendEmailVerificationCommandHandler> logger)
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

    public async Task<ErrorOr<SendEmailVerificationResponse>> Handle(
        SendEmailVerificationCommand request,
        CancellationToken cancellationToken)
    {
        // Get the user
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Check if already verified
        if (user.EmailConfirmed)
        {
            return EmailVerificationErrors.EmailAlreadyVerified;
        }

        // Rate limiting check
        var recentCount = await _tokenRepository.GetRecentTokenCountAsync(
            user.Email,
            _emailSettings.RateLimitWindow,
            cancellationToken);

        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for email verification: {Email}",
                EmailMasking.Mask(user.Email));
            return EmailVerificationErrors.TooManyRequests;
        }

        // Invalidate any existing tokens
        await _tokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate OTP
        var otp = _otpGenerator.GenerateNumericOtp(6);
        var otpHash = _otpHasher.Hash(user.Id.ToString(), otp);

        // With email disabled the log is the only other place the OTP exists,
        // which is what makes the flow testable locally. Gated on the environment
        // as well as the setting: this code IS the credential - presenting it
        // confirms ownership of the address and completes verification with no
        // further proof - and Email:Enabled is a hot setting an operator can flip
        // from the console in production, so on its own it would put a live
        // verification code in the production log. Note the code is not masked
        // the way the address beside it is.
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

        // Send email from the database-managed template. RecipientUserId makes
        // the notification language follow the user's stored PreferredLanguage
        // (the site language chosen at registration) rather than the request culture.
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
                "Failed to send verification email to {UserId}: {Error}",
                user.Id, sendResult.FirstError.Description);
            return EmailVerificationErrors.EmailSendFailed;
        }

        _logger.LogInformation(
            "Verification OTP sent to user {UserId} ({Email})",
            user.Id, EmailMasking.Mask(user.Email));

        return new SendEmailVerificationResponse(token.ExpiresAt, EmailMasking.Mask(user.Email));
    }
}
