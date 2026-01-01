using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands.EmailVerification;

/// <summary>
/// Handler for sending email verification OTP.
/// </summary>
public class SendEmailVerificationCommandHandler
    : IRequestHandler<SendEmailVerificationCommand, ErrorOr<SendEmailVerificationResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly IEmailService _emailService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<SendEmailVerificationCommandHandler> _logger;

    public SendEmailVerificationCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        IEmailService emailService,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        IOptions<EmailSettings> emailSettings,
        ILogger<SendEmailVerificationCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _emailService = emailService;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _emailSettings = emailSettings.Value;
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
                user.Email);
            return EmailVerificationErrors.TooManyRequests;
        }

        // Invalidate any existing tokens
        await _tokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate OTP
        var otp = _otpGenerator.GenerateNumericOtp(6);
        var otpHash = _passwordHasher.HashPassword(otp);

        // Create token
        var token = EmailVerificationToken.Create(
            user.Id,
            otpHash,
            user.Email,
            _emailSettings.OtpExpirationMinutes);

        await _tokenRepository.CreateAsync(token, cancellationToken);

        // Send email
        var recipientName = user.DisplayName ?? user.FirstName ?? "User";
        var emailSent = await _emailService.SendVerificationOtpAsync(
            user.Email,
            recipientName,
            otp,
            _emailSettings.OtpExpirationMinutes,
            cancellationToken);

        if (!emailSent)
        {
            _logger.LogError("Failed to send verification email to {UserId}", user.Id);
            return EmailVerificationErrors.EmailSendFailed;
        }

        _logger.LogInformation(
            "Verification OTP sent to user {UserId} ({Email})",
            user.Id, MaskEmail(user.Email));

        return new SendEmailVerificationResponse(token.ExpiresAt, MaskEmail(user.Email));
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
