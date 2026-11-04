namespace Auth.Application.DTOs;

/// <summary>
/// How many of an application's recent users already hold a qualifying second factor
/// (S23): what the console shows before an administrator switches the application's
/// two-step requirement on.
/// </summary>
public sealed record ApplicationMfaReadinessDto
{
    /// <summary>Gets the distinct users who started a session in the application in the window.</summary>
    public int ActiveUsers { get; init; }

    /// <summary>Gets those whose account has an enabled authenticator app.</summary>
    public int WithQualifyingFactor { get; init; }

    /// <summary>
    /// Gets those who would be asked to set up an authenticator before they can sign in
    /// again. An emailed code never counts.
    /// </summary>
    public int WithoutQualifyingFactor { get; init; }

    /// <summary>Gets the length of the window, in days.</summary>
    public int WindowDays { get; init; }
}
