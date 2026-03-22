namespace Auth.Application.Interfaces;

/// <summary>
/// Generates cryptographically secure URL-safe tokens.
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>
    /// Generates a cryptographically secure URL-safe token.
    /// </summary>
    /// <returns>A URL-safe Base64-encoded token.</returns>
    string Generate();
}
