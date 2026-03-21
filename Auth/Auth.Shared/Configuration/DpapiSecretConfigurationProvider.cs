using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Shared.Configuration;

/// <summary>
/// Configuration source that loads secrets from DPAPI-encrypted file.
/// </summary>
public class DpapiSecretConfigurationSource : IConfigurationSource
{
    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public DpapiSecretConfigurationSource(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DpapiSecretConfigurationProvider(_secretFilePath, _dataProtectionProvider);
    }
}

/// <summary>
/// Configuration provider that loads secrets from DPAPI file and maps them to configuration keys.
/// This enables DPAPI secrets to override values from appsettings.json.
/// </summary>
public class DpapiSecretConfigurationProvider : ConfigurationProvider
{
    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private const string ProtectorPurpose = "AuthSystem.SecretManagement.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DpapiSecretConfigurationProvider(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
    }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_secretFilePath))
        {
            return;
        }

        try
        {
            var encryptedData = File.ReadAllBytes(_secretFilePath);
            var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
            var decryptedBytes = protector.Unprotect(encryptedData);
            var json = Encoding.UTF8.GetString(decryptedBytes);

            var secrets = JsonSerializer.Deserialize<SecretConfiguration>(json, JsonOptions);

            if (secrets != null)
            {
                MapSecretsToConfiguration(secrets);
            }
        }
        catch (CryptographicException)
        {
            // Log warning but don't fail - fall back to appsettings values
            Console.WriteLine($"Warning: Could not decrypt secret file at {_secretFilePath}. Using appsettings.json values.");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: Could not parse secret file at {_secretFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps secrets from the DPAPI file to configuration keys that override appsettings.json.
    /// </summary>
    private void MapSecretsToConfiguration(SecretConfiguration secrets)
    {
        // JWT Settings - Store decrypted PEM directly for JwtTokenService
        if (!string.IsNullOrEmpty(secrets.JwtPrivateKeyPem))
        {
            Data["Jwt:PrivateKeyPem"] = secrets.JwtPrivateKeyPem;
        }

        // Store the raw HMAC key (not DPAPI encrypted) for RefreshTokenKeyService
        // This uses a NEW key that takes priority over the legacy encrypted key
        if (!string.IsNullOrEmpty(secrets.RefreshTokenHmacKey))
        {
            Data["Jwt:RefreshTokenHmacKeyPlain"] = secrets.RefreshTokenHmacKey;
        }

        // Email settings - SMTP password
        if (!string.IsNullOrEmpty(secrets.SmtpPassword))
        {
            Data["Email:Password"] = secrets.SmtpPassword;
        }

        // Gateway settings - Token for inter-service authentication
        // Maps to both Auth API (ExpectedToken) and API Gateway (Token) keys
        if (!string.IsNullOrEmpty(secrets.GatewayToken))
        {
            Data["Gateway:ExpectedToken"] = secrets.GatewayToken;  // Auth API expects this
            Data["Gateway:Token"] = secrets.GatewayToken;          // API Gateway sends this
        }

        // Connection strings (when SQL authentication is used)
        if (!string.IsNullOrEmpty(secrets.ConnectionStrings?.AuthDb))
        {
            Data["ConnectionStrings:AuthDb"] = secrets.ConnectionStrings.AuthDb;
        }

        // Custom secrets under Secrets:Custom:* namespace
        foreach (var (key, value) in secrets.Custom)
        {
            Data[$"Secrets:Custom:{key}"] = value;
        }
    }
}
