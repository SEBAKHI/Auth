using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// A fresh TOTP secret, in the three forms an authenticator app is given it.
/// </summary>
/// <param name="Secret">The secret, base32, before it is encrypted for storage.</param>
/// <param name="QrCodeUri">The <c>otpauth://</c> URI the QR code encodes.</param>
/// <param name="ManualEntryKey">The secret grouped in fours, for typing in by hand.</param>
public sealed record AuthenticatorKey(string Secret, string QrCodeUri, string ManualEntryKey)
{
    // The secret stays out of any log line or assertion message the key reaches.
    public override string ToString() => "AuthenticatorKey { … }";
}

/// <summary>
/// Issues the secret a new authenticator app is set up with — the first one
/// (setup) or a replacement — labelled the same way both times.
/// </summary>
public class AuthenticatorKeyFactory
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly ITotpService _totpService;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthenticatorKeyFactory> _logger;

    public AuthenticatorKeyFactory(
        IPlatformSettingsRepository platformSettingsRepository,
        ITotpService totpService,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<AuthenticatorKeyFactory> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _totpService = totpService;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a secret and the QR code URI that names the account by its address.
    /// </summary>
    /// <param name="accountEmail">The address the authenticator app lists the account under.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AuthenticatorKey> CreateAsync(string accountEmail, CancellationToken cancellationToken)
    {
        var secret = _totpService.GenerateSecret();
        var issuer = await ResolveIssuerAsync(cancellationToken);
        var qrCodeUri = _totpService.GenerateQrCodeUri(secret, accountEmail, issuer);

        return new AuthenticatorKey(secret, qrCodeUri, FormatManualEntryKey(secret));
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
