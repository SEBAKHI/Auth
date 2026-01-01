using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands.TwoFactor;

/// <summary>
/// Handler for the setup two-factor authentication command.
/// </summary>
public class SetupTwoFactorCommandHandler : IRequestHandler<SetupTwoFactorCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorAuthRepository _twoFactorRepository;
    private readonly ITotpService _totpService;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<SetupTwoFactorCommandHandler> _logger;

    public SetupTwoFactorCommandHandler(
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        ITotpService totpService,
        IOptions<JwtSettings> jwtSettings,
        ILogger<SetupTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _totpService = totpService;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorSetupResponse>> Handle(
        SetupTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // Get the user
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Check if 2FA is already enabled
        var existing = await _twoFactorRepository.GetByUserIdAsync(request.UserId, cancellationToken);
        if (existing?.IsEnabled == true)
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        // Generate new secret
        var secret = _totpService.GenerateSecret();

        // Generate QR code URI
        var issuer = _jwtSettings.Issuer ?? "AuthSystem";
        var qrCodeUri = _totpService.GenerateQrCodeUri(secret, user.Email, issuer);

        // Store the secret (not yet enabled)
        if (existing != null)
        {
            // Update existing setup with new secret
            await _twoFactorRepository.DeleteAsync(request.UserId, cancellationToken);
        }

        var twoFactorAuth = TwoFactorAuth.Create(request.UserId, secret);
        await _twoFactorRepository.CreateAsync(twoFactorAuth, cancellationToken);

        _logger.LogInformation(
            "Two-factor authentication setup initiated for user {UserId}",
            request.UserId);

        return new TwoFactorSetupResponse(
            Secret: secret,
            QrCodeUri: qrCodeUri,
            ManualEntryKey: FormatManualEntryKey(secret));
    }

    private static string FormatManualEntryKey(string secret)
    {
        // Format for easier manual entry: XXXX-XXXX-XXXX-XXXX-...
        var chars = secret.ToCharArray();
        var formatted = new System.Text.StringBuilder();

        for (int i = 0; i < chars.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                formatted.Append(' ');
            }
            formatted.Append(chars[i]);
        }

        return formatted.ToString();
    }
}
