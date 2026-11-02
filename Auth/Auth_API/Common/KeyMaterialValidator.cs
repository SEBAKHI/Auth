using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Infrastructure.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Common;

/// <summary>
/// The signing key and the gateway token, checked at boot as part of the "key material
/// validation" startup step (X03), next to S11's HMAC keyring. Until X03 neither was checked
/// at boot: a key that could not sign failed the first sign-in, and a short gateway token was
/// accepted although the import endpoint refuses one.
/// </summary>
public static class KeyMaterialValidator
{
    private const string GatewayTokenKey = "Gateway:ExpectedToken";

    /// <exception cref="InvalidOperationException">The material is unusable; the message names it and the way out.</exception>
    public static void Validate(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        bool isDevelopment)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        // Outside Development a configured file is never silently replaced by the next source:
        // that is how a host would start signing with a key nobody chose.
        if (!isDevelopment
            && !string.IsNullOrWhiteSpace(jwtSettings.PrivateKeyPath)
            && !File.Exists(jwtSettings.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                $"Refusing to start: {SigningKeyLoader.PathKey} is set to '{jwtSettings.PrivateKeyPath}', but no file " +
                "exists there. Fix the path, or remove the setting so the signing key in the secrets file is used.");
        }

        // The same loader the token service is built with, so what passes here is what signs.
        SigningKeyLoader.Load(jwtSettings, dataProtectionProvider).Dispose();

        var gateway = SecretKeyMaterial.ValidateGatewayToken(configuration[GatewayTokenKey] ?? string.Empty);
        if (gateway.IsError)
        {
            throw new InvalidOperationException(
                $"Refusing to start: {GatewayTokenKey} is unusable. {gateway.FirstError.Description} Set a token of " +
                $"at least {SecretKeyMaterial.MinimumGatewayTokenLength} characters where it came from (the secrets " +
                "file in Certificate/Dpapi mode), and give the API Gateway the same value as Gateway:Token.");
        }
    }
}
