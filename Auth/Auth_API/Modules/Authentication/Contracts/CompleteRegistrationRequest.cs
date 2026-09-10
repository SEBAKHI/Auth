using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The third step of a self-registration: the handle, the code once more, and
/// what the account needs. No email field, by contract — the handle fixes the
/// address — and no phone number: it is written on another connection after
/// the account row exists and returns as a profile edit after sign-in.
/// </summary>
public record CompleteRegistrationRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string PendingId { get; init; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    [RegularExpression(@"^\d{6}$")]
    public required string Otp { get; init; }

    [Required]
    public required string Password { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string FirstName { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string LastName { get; init; }

    [StringLength(50)]
    public string? TimeZone { get; init; }

    public bool CreateOrganization { get; init; } = false;

    public string? DeviceId { get; init; }
}
