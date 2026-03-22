using System.Security.Cryptography;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Generates cryptographically secure URL-safe tokens using 256 bits of entropy.
/// </summary>
public class SecureTokenGenerator : ISecureTokenGenerator
{
    /// <inheritdoc />
    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
