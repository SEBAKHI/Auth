namespace Auth_Lib.Application.Interfaces;

/// <summary>
/// Service for generating one-time passwords.
/// </summary>
public interface IOtpGenerator
{
    /// <summary>
    /// Generates a cryptographically secure numeric OTP.
    /// </summary>
    /// <param name="digits">Number of digits (default 6).</param>
    /// <returns>A numeric string of the specified length.</returns>
    string GenerateNumericOtp(int digits = 6);
}
