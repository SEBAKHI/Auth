namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The second step of a self-registration: the handle the start step answered
/// with, and the code that reached the address. No email field, by contract —
/// the address is fixed by the handle, which is what makes it uneditable on the
/// screens that follow.
/// </summary>
public record VerifyRegistrationRequest
{
    public string PendingId { get; init; } = string.Empty;

    public string Otp { get; init; } = string.Empty;
}
