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
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ResendEmailVerificationCommandHandler> _logger;

    public ResendEmailVerificationCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        IOptions<EmailSettings> emailSettings,
        ILogger<ResendEmailVerificationCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _emailSettings = emailSettings.Value;
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
                MaskEmail(request.Email));

            // Return fake response to prevent enumeration
            return new ResendEmailVerificationResponse(
                DateTime.UtcNow.AddMinutes(_emailSettings.OtpExpirationMinutes),
                MaskEmail(request.Email));
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
                "Rate limit exceeded for email verification resend: {Email}",
                MaskEmail(user.Email));
            return EmailVerificationErrors.TooManyRequests;
        }

        // Invalidate any existing tokens
        await _tokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate OTP
        var otp = _otpGenerator.GenerateNumericOtp(6);
        var otpHash = _passwordHasher.HashPassword(otp);

        // Log OTP when email is disabled (development mode)
        if (!_emailSettings.Enabled)
        {
            _logger.LogWarning(
                "Email disabled - OTP for {Email}: {Otp} (expires in {Minutes} minutes)",
                MaskEmail(user.Email), otp, _emailSettings.OtpExpirationMinutes);
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
            user.Id, MaskEmail(user.Email));

        return new ResendEmailVerificationResponse(token.ExpiresAt, MaskEmail(user.Email));
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
            return $"{localPart[0]}***{domain}";

        return $"{localPart[0]}{new string('*', Math.Min(localPart.Length - 2, 4))}{localPart[^1]}{domain}";
    }
}
