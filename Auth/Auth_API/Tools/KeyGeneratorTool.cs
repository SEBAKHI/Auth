using System.Security.Cryptography;
using Auth_Lib.Infrastructure.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Auth_API.Tools;

/// <summary>
/// Utility to generate and encrypt the HMAC key using Windows DPAPI.
/// Run once during initial setup to generate the RefreshTokenEncryptedKey.
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
    /// Prints usage instructions for generating and configuring the HMAC key.
    /// </summary>
    public static void PrintInstructions()
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
}
