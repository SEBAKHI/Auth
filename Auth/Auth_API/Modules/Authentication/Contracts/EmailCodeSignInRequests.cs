namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The first step of a sign-in by emailed code (OI-215), and of a sign-up by code: an
/// address, and nothing else.
/// </summary>
public record StartEmailCodeSignInRequest
{
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The site language the visitor is using: the language of a message to an
    /// address without an account, and of a new account made from it.
    /// </summary>
    public string? PreferredLanguage { get; init; }
}

/// <summary>
/// The code of a sign-in by emailed code, checked without being spent (OI-215). No
/// address: the handle fixes it.
/// </summary>
public record VerifyEmailCodeSignInRequest
{
    public string PendingId { get; init; } = string.Empty;

    public string Otp { get; init; } = string.Empty;
}

/// <summary>
/// The code of a sign-in by emailed code, presented to finish the sign-in (OI-215).
/// </summary>
public record SignInWithEmailCodeRequest
{
    public string PendingId { get; init; } = string.Empty;

    public string Otp { get; init; } = string.Empty;

    /// <summary>
    /// Add-only: an account with a password proves it too before the sign-in finishes,
    /// as the "sign in again" dialog asks before a security change. It can only make
    /// the sign-in harder.
    /// </summary>
    public bool RequireFullProof { get; init; }

    /// <summary>The calling browser's own identifier.</summary>
    public string? DeviceId { get; init; }

    /// <summary>The person's "Stay signed in" choice (OI-154).</summary>
    public bool StaySignedIn { get; init; }
}

/// <summary>
/// The code of a sign-in by emailed code, presented to recover an account waiting for
/// deletion (OI-215), with the authenticator app's code when the account has the app.
/// </summary>
public record RecoverAccountWithEmailCodeRequest
{
    public string PendingId { get; init; } = string.Empty;

    public string Otp { get; init; } = string.Empty;

    /// <summary>The authenticator app's code, or a recovery code.</summary>
    public string? TwoFactorCode { get; init; }

    /// <summary>Add-only, as at sign-in.</summary>
    public bool RequireFullProof { get; init; }

    /// <summary>The calling browser's own identifier.</summary>
    public string? DeviceId { get; init; }
}
