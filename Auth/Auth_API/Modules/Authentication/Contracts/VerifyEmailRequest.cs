using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for verifying email with OTP.
/// </summary>
public record VerifyEmailRequest
{
    /// <summary>
    /// Gets the user ID to verify email for. Either this or <see cref="Email"/> must be provided.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Gets the email address to verify. Either this or <see cref="UserId"/> must be provided.
    /// </summary>
    [EmailAddress]
    public string? Email { get; init; }

    /// <summary>
    /// Gets the 6-digit OTP code sent to the user's email.
    /// </summary>
    [Required]
    [StringLength(6, MinimumLength = 6)]
    [RegularExpression(@"^\d{6}$")]
    public required string Otp { get; init; }

    /// <summary>
    /// Gets the optional client device identifier. Used to tag the session when
    /// the anonymous (email-keyed) path signs the user in on success.
    /// </summary>
    public string? DeviceId { get; init; }
}
