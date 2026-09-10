using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The second step of a self-registration: the handle the start step answered
/// with, and the code that reached the address. No email field, by contract —
/// the address is fixed by the handle, which is what makes it uneditable on the
/// screens that follow.
/// </summary>
public record VerifyRegistrationRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string PendingId { get; init; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    [RegularExpression(@"^\d{6}$")]
    public required string Otp { get; init; }
}
