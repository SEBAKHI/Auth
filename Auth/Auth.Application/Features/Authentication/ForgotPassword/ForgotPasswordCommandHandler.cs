using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ForgotPassword;

/// <summary>
/// Handler for the forgot password command.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ErrorOr<ForgotPasswordResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        ISecureTokenGenerator tokenGenerator,
        IRefreshTokenKeyService tokenKeyService,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _tokenGenerator = tokenGenerator;
        _tokenKeyService = tokenKeyService;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<ForgotPasswordResponse>> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        var expirationMinutes = _emailSettings.ResetTokenExpirationMinutes;

        // Always return success to prevent email enumeration attacks
        // Even if the user doesn't exist, we return a fake successful response
        if (user == null)
        {
            _logger.LogInformation(
                "Password reset requested for non-existent email: {Email}",
                MaskEmail(request.Email));

            // Return fake response to prevent enumeration
            return new ForgotPasswordResponse(
                DateTime.UtcNow.AddMinutes(expirationMinutes),
                MaskEmail(request.Email));
        }

        // Invalidate any existing reset tokens for this user
        await _passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate a new 256-bit token. It is hashed with HMAC-SHA256 rather than
        // Argon2id: the token is high-entropy so it cannot be guessed, and a
        // deterministic hash is what lets the token alone identify the row on
        // redemption - no email required. Mirrors RefreshTokens.
        var token = _tokenGenerator.Generate();
        var tokenHash = _tokenKeyService.ComputeTokenHash(token);

        // Create and store the reset token
        var resetToken = PasswordResetToken.Create(
            user.Id,
            tokenHash,
            expirationMinutes);

        await _passwordResetTokenRepository.CreateAsync(resetToken, cancellationToken);

        // Log the reset link when email is disabled (development mode); the email
        // is the only other place it exists.
        if (!_emailSettings.Enabled)
        {
            _logger.LogWarning(
                "Email disabled - Password reset link for {Email}: {ResetUrl} (expires in {Minutes} minutes)",
                MaskEmail(user.Email), _emailSettings.BuildPasswordResetUrl(token), expirationMinutes);
        }

        var recipientName = user.DisplayName ?? user.FirstName ?? "User";
        var emailSent = await _emailService.SendPasswordResetAsync(
            user.Email,
            recipientName,
            token,
            expirationMinutes,
            cancellationToken);

        // Anti-enumeration: the response stays a generic success even when the email
        // could not be delivered; the plaintext token is never returned to the caller.
        if (!emailSent)
        {
            _logger.LogError(
                "Failed to send password reset email to user {UserId}",
                user.Id);
        }

        _logger.LogInformation(
            "Password reset token generated for user {UserId}",
            user.Id);

        return new ForgotPasswordResponse(resetToken.ExpiresAt, MaskEmail(user.Email));
    }

    /// <summary>
    /// Masks an email address for safe display (e.g., a****n@example.com).
    /// </summary>
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
