using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ForgotPassword;

/// <summary>
/// Handler for the forgot password command: the anonymous way to ask for a reset link. The link
/// itself is issued by <see cref="IPasswordResetLinkIssuer"/>, which the two signed-in ways share
/// (S13, D7); this decides only the answer, which is the same for every address.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ErrorOr<ForgotPasswordResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetLinkIssuer _linkIssuer;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetLinkIssuer linkIssuer,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _linkIssuer = linkIssuer;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<ForgotPasswordResponse>> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        // The mask the caller sees is derived from the normalized input in every
        // branch. Masking the raw input for an unknown address and the stored
        // address for a known one let the letter case of the reply say which
        // branch ran.
        var maskedEmail = EmailMasking.Mask(request.Email.Trim().ToLowerInvariant());

        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // Always return success to prevent email enumeration attacks
        // Even if the user doesn't exist, we return a fake successful response
        if (user == null)
        {
            _logger.LogInformation(
                "Password reset requested for non-existent email: {Email}",
                maskedEmail);

            // Return fake response to prevent enumeration
            return new ForgotPasswordResponse(
                DateTime.UtcNow.AddMinutes(_emailSettings.ResetTokenExpirationMinutes),
                maskedEmail);
        }

        var issue = await _linkIssuer.IssueAsync(user, cancellationToken);
        return new ForgotPasswordResponse(issue.ExpiresAt, maskedEmail);
    }
}
