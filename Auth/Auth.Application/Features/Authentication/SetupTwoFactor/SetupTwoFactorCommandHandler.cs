using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.SetupTwoFactor;

/// <summary>
/// Handler for the setup two-factor authentication command.
/// </summary>
/// <remarks>
/// Setup hands a fresh secret to the caller, so it asks for a recent sign-in
/// first. The secret replaces the pending one in place: the failure count and
/// lock that guessing at enable earned stay, so starting setup again does not
/// clear them, and a factor already in use is never touched.
/// </remarks>
public class SetupTwoFactorCommandHandler : IRequestHandler<SetupTwoFactorCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorSecretProtector _secretProtector;
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly ITotpService _totpService;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<SetupTwoFactorCommandHandler> _logger;

    public SetupTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorSecretProtector secretProtector,
        IPlatformSettingsRepository platformSettingsRepository,
        ITotpService totpService,
        FirstFactorEmailProofPolicy emailProofPolicy,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<SetupTwoFactorCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _secretProtector = secretProtector;
        _platformSettingsRepository = platformSettingsRepository;
        _totpService = totpService;
        _emailProofPolicy = emailProofPolicy;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorSetupResponse>> Handle(
        SetupTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // A recent sign-in, before a secret is generated, stored or returned.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Generate new secret
        var secret = _totpService.GenerateSecret();

        // Generate QR code URI
        var issuer = await ResolveIssuerAsync(cancellationToken);
        var qrCodeUri = _totpService.GenerateQrCodeUri(secret, user.Email, issuer);

        // Stored encrypted on the pending row: replaced in place, or inserted when
        // there is none. An enabled factor matches nothing, and the secret is not
        // returned.
        var protectedSecret = await _secretProtector.ProtectAsync(request.UserId, secret, cancellationToken);
        if (!await _twoFactorStateStore.TryStorePendingSecretAsync(request.UserId, protectedSecret, cancellationToken))
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        _logger.LogInformation(
            "Two-factor authentication setup initiated for user {UserId}",
            request.UserId);

        // Only a pending factor reaches here, so this is the account's first: enable
        // will want the emailed code exactly when the rule applies right now. A rule
        // that changes before enable is answered there (TwoFactor.EmailCodeRequired).
        return new TwoFactorSetupResponse(
            Secret: secret,
            QrCodeUri: qrCodeUri,
            ManualEntryKey: FormatManualEntryKey(secret),
            EmailCodeRequired: _emailProofPolicy.IsRequired);
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
