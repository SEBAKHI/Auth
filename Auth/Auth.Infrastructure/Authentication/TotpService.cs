using System.Security.Cryptography;
using System.Web;
using Auth.Application.Interfaces;
using OtpNet;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Implementation of TOTP service using OtpNet.
/// </summary>
public class TotpService : ITotpService
{
    private const int SecretSize = 20; // 160 bits
    private const int TotpSize = 6;
    private const int TotpStep = 30; // seconds
    private const int RecoveryCodeLength = 8;

    private readonly IPasswordHasher _passwordHasher;

    public TotpService(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public string GenerateSecret()
    {
        var secretBytes = new byte[SecretSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(secretBytes);
        }
        return Base32Encoding.ToString(secretBytes);
    }

    /// <inheritdoc />
    public string GenerateQrCodeUri(string secret, string email, string issuer)
    {
        var encodedIssuer = HttpUtility.UrlEncode(issuer);
        var encodedEmail = HttpUtility.UrlEncode(email);

        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits={TotpSize}&period={TotpStep}";
    }

    /// <inheritdoc />
    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != TotpSize)
        {
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: TotpStep, totpSize: TotpSize);

            // Allow for time drift by checking adjacent windows
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string[] GenerateRecoveryCodes(int count = 10)
    {
        var codes = new string[count];
        using var rng = RandomNumberGenerator.Create();

        for (int i = 0; i < count; i++)
        {
            codes[i] = GenerateRecoveryCode(rng);
        }

        return codes;
    }

    /// <inheritdoc />
    public string HashRecoveryCode(string code)
    {
        // Normalize the code (remove any spaces or dashes)
        var normalized = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

        // Hash using Argon2id for secure storage
        return _passwordHasher.HashPassword(normalized);
    }

    /// <inheritdoc />
    public bool VerifyRecoveryCode(string code, string hash)
    {
        // Normalize the code (remove any spaces or dashes)
        var normalized = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

        // Verify using Argon2id
        return _passwordHasher.VerifyPassword(normalized, hash);
    }

    private static string GenerateRecoveryCode(RandomNumberGenerator rng)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Exclude similar characters
        var bytes = new byte[RecoveryCodeLength];
        rng.GetBytes(bytes);

        var code = new char[RecoveryCodeLength];
        for (int i = 0; i < RecoveryCodeLength; i++)
        {
            code[i] = chars[bytes[i] % chars.Length];
        }

        // Format as XXXX-XXXX
        var codeStr = new string(code);
        return $"{codeStr[..4]}-{codeStr[4..]}";
    }
}
