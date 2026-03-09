using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Handler for verifying email using OTP.
/// </summary>
public class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        IPasswordHasher passwordHasher,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
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

        // Get the user
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return EmailVerificationErrors.UserNotFound;
        }

        // Check if already verified
        if (user.EmailConfirmed)
        {
            return EmailVerificationErrors.EmailAlreadyVerified;
        }

        // Get valid token for user
        var token = await _tokenRepository.GetValidTokenForUserAsync(request.UserId, cancellationToken);
        if (token == null)
        {
            _logger.LogWarning(
                "No valid verification token found for user {UserId}",
                request.UserId);
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // Check attempt count
        if (token.AttemptCount >= EmailVerificationToken.MaxAttempts)
        {
            _logger.LogWarning(
                "Max verification attempts exceeded for user {UserId}",
                request.UserId);
            return EmailVerificationErrors.TooManyAttempts;
        }

        // Verify OTP using Argon2id
        var isValid = _passwordHasher.VerifyPassword(request.Otp, token.OtpHash);

        if (!isValid)
        {
            // Increment attempt count
            await _tokenRepository.IncrementAttemptCountAsync(token.Id, cancellationToken);

            var remainingAttempts = EmailVerificationToken.MaxAttempts - token.AttemptCount - 1;
            _logger.LogWarning(
                "Invalid OTP for user {UserId}. Remaining attempts: {RemainingAttempts}",
                request.UserId, remainingAttempts);

            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // OTP is valid - mark token as used and confirm email
        await _tokenRepository.MarkAsUsedAsync(token.Id, cancellationToken);
        await _userRepository.ConfirmEmailAsync(user.Id, user.Id, cancellationToken);

        _logger.LogInformation(
            "Email verified successfully for user {UserId} ({Email})",
            user.Id, user.Email);

        return Result.Success;
    }
}
