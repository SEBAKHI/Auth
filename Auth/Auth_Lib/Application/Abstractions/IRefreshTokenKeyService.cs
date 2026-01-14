using Microsoft.AspNetCore.DataProtection;

namespace Auth_Lib.Application.Abstractions;

/// <summary>
/// Service for managing HMAC key operations for refresh tokens.
/// The HMAC key is protected at rest using Windows DPAPI (Data Protection API).
/// </summary>
public interface IRefreshTokenKeyService
{
    /// <summary>
    /// Computes HMAC-SHA256 hash of a refresh token for secure database storage.
    /// </summary>
    /// <param name="token">The plain text refresh token.</param>
    /// <returns>The HMAC-SHA256 hash as a base64 string.</returns>
    string ComputeTokenHash(string token);
}
