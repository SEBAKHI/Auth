namespace Auth_Lib.Application.Interfaces;

/// <summary>
/// Service for Time-based One-Time Password (TOTP) operations.
/// </summary>
public interface ITotpService
{
    /// <summary>
    /// Generates a new TOTP secret key.
    /// </summary>
    /// <returns>Base32-encoded secret key.</returns>
    string GenerateSecret();

    /// <summary>
    /// Generates a URI for authenticator app setup (otpauth://).
    /// </summary>
    /// <param name="secret">The TOTP secret key.</param>
    /// <param name="email">The user's email address.</param>
    /// <param name="issuer">The application name.</param>
    /// <returns>URI for QR code generation.</returns>
    string GenerateQrCodeUri(string secret, string email, string issuer);

    /// <summary>
    /// Validates a TOTP code against the secret.
    /// </summary>
    /// <param name="secret">The TOTP secret key.</param>
    /// <param name="code">The code to validate.</param>
    /// <returns>True if the code is valid.</returns>
    bool ValidateCode(string secret, string code);

    /// <summary>
    /// Generates recovery codes for backup access.
    /// </summary>
    /// <param name="count">Number of codes to generate.</param>
    /// <returns>Array of recovery codes.</returns>
    string[] GenerateRecoveryCodes(int count = 10);

    /// <summary>
    /// Hashes a recovery code for storage.
    /// </summary>
    /// <param name="code">The plain recovery code.</param>
    /// <returns>Hashed recovery code.</returns>
    string HashRecoveryCode(string code);

    /// <summary>
    /// Verifies a recovery code against a hash.
    /// </summary>
    /// <param name="code">The plain recovery code.</param>
    /// <param name="hash">The hashed recovery code.</param>
    /// <returns>True if the code matches.</returns>
    bool VerifyRecoveryCode(string code, string hash);
}
