namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The third step of a self-registration: the handle, the code once more, and
/// what the account needs. No email field, by contract — the handle fixes the
/// address — and no phone number: it is written on another connection after
/// the account row exists and returns as a profile edit after sign-in.
/// </summary>
public record CompleteRegistrationRequest
{
    public string PendingId { get; init; } = string.Empty;

    public string Otp { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string? TimeZone { get; init; }

    public bool CreateOrganization { get; init; } = false;

    public string? DeviceId { get; init; }
}
