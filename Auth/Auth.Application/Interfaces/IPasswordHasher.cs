namespace Auth.Application.Interfaces;

/// <summary>
/// Interface for password hashing operations.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a password using Argon2id.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>The encoded hash string containing algorithm parameters, salt, and hash.</returns>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a password against a stored hash.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="storedHash">The stored encoded hash string.</param>
    /// <returns>True if the password matches, false otherwise.</returns>
    bool VerifyPassword(string password, string storedHash);

    /// <summary>
    /// Checks if a hash needs to be rehashed (e.g., if parameters have changed).
    /// </summary>
    /// <param name="storedHash">The stored encoded hash string.</param>
    /// <returns>True if the hash should be rehashed, false otherwise.</returns>
    bool NeedsRehash(string storedHash);
}
