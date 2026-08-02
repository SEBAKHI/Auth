using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.SetupTwoFactor;

/// <summary>
/// Handler for the setup two-factor authentication command.
/// </summary>
public class SetupTwoFactorCommandHandler : IRequestHandler<SetupTwoFactorCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorAuthRepository _twoFactorRepository;
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly ITotpService _totpService;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<SetupTwoFactorCommandHandler> _logger;

    public SetupTwoFactorCommandHandler(
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        IPlatformSettingsRepository platformSettingsRepository,
        ITotpService totpService,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<SetupTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _platformSettingsRepository = platformSettingsRepository;
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
        var issuer = await ResolveIssuerAsync(cancellationToken);
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

    /// <summary>
    /// The issuer is what an authenticator app shows as the account's provider,
    /// so it has to read as a name. <c>Jwt:Issuer</c> is a URL — using it put
    /// "https://auth.example.com" in the app's list, percent-encoded, and the
    /// encoded "://" inside the otpauth label trips stricter parsers. The
    /// platform's display name is the same identity the branding and the
    /// transactional emails already use.
    /// </summary>
    private async Task<string> ResolveIssuerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var platform = await _platformSettingsRepository.GetAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(platform?.PlatformName))
            {
                return platform.PlatformName.Trim();
            }
        }
        catch (Exception ex)
        {
            // Branding is not worth failing an enrolment over.
            _logger.LogWarning(ex, "Could not read the platform name for the TOTP issuer");
        }

        // Fall back to the issuer's host rather than the whole URL, so the
        // account label stays readable even when branding is unavailable.
        if (Uri.TryCreate(_jwtSettings.Issuer, UriKind.Absolute, out var issuerUri))
        {
            return issuerUri.Host;
        }

        return string.IsNullOrWhiteSpace(_jwtSettings.Issuer)
            ? "AuthSystem"
            : _jwtSettings.Issuer;
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
