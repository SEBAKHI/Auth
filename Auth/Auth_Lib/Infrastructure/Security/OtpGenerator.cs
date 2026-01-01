using System.Security.Cryptography;
using Auth_Lib.Application.Abstractions;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// Implementation of OTP generator using cryptographic random number generation.
/// </summary>
public class OtpGenerator : IOtpGenerator
{
    /// <inheritdoc />
    public string GenerateNumericOtp(int digits = 6)
    {
        if (digits < 4 || digits > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be between 4 and 10.");
        }

        // Calculate the maximum value (e.g., 999999 for 6 digits)
        var maxValue = (int)Math.Pow(10, digits);

        // Generate a random number within the range
        var randomNumber = RandomNumberGenerator.GetInt32(0, maxValue);

        // Pad with leading zeros if necessary
        return randomNumber.ToString($"D{digits}");
    }
}
