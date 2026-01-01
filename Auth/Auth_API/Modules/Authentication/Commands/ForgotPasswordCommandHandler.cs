using System.Security.Cryptography;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the forgot password command.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ErrorOr<ForgotPasswordResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    // Token expiration in minutes
    private const int TokenExpirationMinutes = 60;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHasher passwordHasher,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHasher = passwordHasher;
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
                request.Email);

            // Return fake response to prevent enumeration
            return new ForgotPasswordResponse(
                GenerateSecureToken(),
                DateTime.UtcNow.AddMinutes(TokenExpirationMinutes));
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

        _logger.LogInformation(
            "Password reset token generated for user {UserId}",
            user.Id);

        // In a production system, the token would be sent via email
        // For now, we return it directly (useful for testing)
        return new ForgotPasswordResponse(token, resetToken.ExpiresAt);
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
}
