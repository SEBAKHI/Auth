using System.Security.Cryptography;
using Auth_Lib.Infrastructure.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Auth_API.Tools;

/// <summary>
/// Utility to generate and encrypt cryptographic keys using Windows DPAPI.
/// Run once during initial setup to generate the required keys.
/// </summary>
public static class KeyGeneratorTool
{
    /// <summary>
    /// Generates a new HMAC key and encrypts it using DPAPI.
    /// The encrypted key should be stored in appsettings.json under JwtSettings.RefreshTokenEncryptedKey.
    /// </summary>
    /// <param name="provider">The data protection provider.</param>
    /// <returns>The DPAPI-encrypted HMAC key as a base64 string.</returns>
    public static string GenerateEncryptedHmacKey(IDataProtectionProvider provider)
    {
        return RefreshTokenKeyService.GenerateEncryptedKey(provider);
    }

    /// <summary>
    /// Generates a new RSA-2048 key pair and encrypts the private key using DPAPI.
    /// The encrypted private key should be stored in appsettings.json under JwtSettings.PrivateKeyEncrypted.
    /// </summary>
    /// <param name="provider">The data protection provider.</param>
    /// <returns>A tuple containing the encrypted private key and the public key PEM.</returns>
    public static (string EncryptedPrivateKey, string PublicKeyPem) GenerateEncryptedRsaKey(
        IDataProtectionProvider provider)
    {
        return RsaKeyService.GenerateEncryptedKeyPair(provider);
    }

    /// <summary>
    /// Prints usage instructions for generating and configuring the HMAC key.
    /// </summary>
    public static void PrintHmacKeyInstructions()
    {
        Console.WriteLine("=== HMAC Key Generation for Refresh Tokens ===");
        Console.WriteLine();
        Console.WriteLine("This utility generates a DPAPI-encrypted HMAC key for secure refresh token hashing.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  1. Run the application with the '--generate-hmac-key' flag");
        Console.WriteLine("  2. Copy the generated encrypted key");
        Console.WriteLine("  3. Add it to appsettings.json under:");
        Console.WriteLine("     \"Jwt\": {");
        Console.WriteLine("       \"RefreshTokenEncryptedKey\": \"<paste-encrypted-key-here>\"");
        Console.WriteLine("     }");
        Console.WriteLine();
        Console.WriteLine("IMPORTANT:");
        Console.WriteLine("  - The key is encrypted using Windows DPAPI");
        Console.WriteLine("  - It can only be decrypted on the same machine (or machines sharing the key ring)");
        Console.WriteLine("  - For multi-server deployments, configure shared Data Protection key storage");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints usage instructions for generating and configuring the RSA key.
    /// </summary>
    public static void PrintRsaKeyInstructions()
    {
        Console.WriteLine("=== RSA Key Generation for JWT Signing ===");
        Console.WriteLine();
        Console.WriteLine("This utility generates a DPAPI-encrypted RSA-2048 key pair for JWT signing.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  1. Run the application with the '--generate-rsa-key' flag");
        Console.WriteLine("  2. Copy the generated encrypted private key");
        Console.WriteLine("  3. Add it to appsettings.json under:");
        Console.WriteLine("     \"Jwt\": {");
        Console.WriteLine("       \"PrivateKeyEncrypted\": \"<paste-encrypted-key-here>\"");
        Console.WriteLine("     }");
        Console.WriteLine();
        Console.WriteLine("The public key is also displayed for external token validation.");
        Console.WriteLine();
        Console.WriteLine("IMPORTANT:");
        Console.WriteLine("  - The private key is encrypted using Windows DPAPI");
        Console.WriteLine("  - It can only be decrypted on the same machine (or machines sharing the key ring)");
        Console.WriteLine("  - For multi-server deployments, configure shared Data Protection key storage");
        Console.WriteLine("  - The public key can be safely shared for external token validation");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints usage instructions for generating and configuring the HMAC key.
    /// </summary>
    [Obsolete("Use PrintHmacKeyInstructions() instead")]
    public static void PrintInstructions() => PrintHmacKeyInstructions();
}
