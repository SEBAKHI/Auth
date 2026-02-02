using System.Security.Cryptography;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the forgot password command.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ErrorOr<ForgotPasswordResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    // Token expiration in minutes
    private const int TokenExpirationMinutes = 60;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHasher passwordHasher,
        IOptions<EmailSettings> emailSettings,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHasher = passwordHasher;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<ForgotPasswordResponse>> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // Always return success to prevent email enumeration attacks
        // Even if the user doesn't exist, we return a fake successful response
        if (user == null)
        {
            _logger.LogInformation(
                "Password reset requested for non-existent email: {Email}",
                MaskEmail(request.Email));

            // Return fake response to prevent enumeration
            return new ForgotPasswordResponse(
                DateTime.UtcNow.AddMinutes(TokenExpirationMinutes),
                MaskEmail(request.Email));
        }

        // Invalidate any existing reset tokens for this user
        await _passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate a new secure token and hash it with Argon2id
        var token = GenerateSecureToken();
        var tokenHash = _passwordHasher.HashPassword(token);

        // Create and store the reset token
        var resetToken = PasswordResetToken.Create(
            user.Id,
            tokenHash,
            TokenExpirationMinutes);

        await _passwordResetTokenRepository.CreateAsync(resetToken, cancellationToken);

        // Log token when email is disabled (development mode)
        if (!_emailSettings.Enabled)
        {
            _logger.LogWarning(
                "Email disabled - Password reset token for {Email}: {Token} (expires in {Minutes} minutes)",
                MaskEmail(user.Email), token, TokenExpirationMinutes);
        }

        _logger.LogInformation(
            "Password reset token generated for user {UserId}",
            user.Id);

        // Token is sent via email in production, logged to console in development
        return new ForgotPasswordResponse(resetToken.ExpiresAt, MaskEmail(user.Email));
    }

    /// <summary>
    /// Generates a cryptographically secure random token.
    /// </summary>
    private static string GenerateSecureToken()
    {
        // Generate 32 bytes of random data
        var randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        // Convert to URL-safe base64
        return Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
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
