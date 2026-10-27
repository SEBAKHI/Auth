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

    /// <summary>
    /// Required unless <c>Registration:PasswordAtSignUp</c> is off and email is on
    /// (OI-215); the code sign-in's verify step says which.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>Required unless <c>Registration:NameAtSignUp</c> is off (OI-215).</summary>
    public string? FirstName { get; init; }

    /// <summary>Required unless <c>Registration:NameAtSignUp</c> is off (OI-215).</summary>
    public string? LastName { get; init; }

    public string? TimeZone { get; init; }

    public bool CreateOrganization { get; init; } = false;

    public string? DeviceId { get; init; }
}
