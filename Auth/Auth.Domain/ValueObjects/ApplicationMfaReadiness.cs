namespace Auth.Domain.ValueObjects;

/// <summary>
/// How ready an application's recent users are for a two-step requirement (S23, AM11):
/// of the people who started a session in the application within the window, how many
/// already hold a qualifying second factor. Read before an administrator switches the
/// requirement on, and before <c>Enforce</c>.
/// </summary>
/// <param name="ActiveUsers">Distinct users with a session in the application within the window.</param>
/// <param name="WithQualifyingFactor">Of those, the users whose account has an enabled authenticator app.</param>
public sealed record ApplicationMfaReadiness(int ActiveUsers, int WithQualifyingFactor)
{
    /// <summary>
    /// The users who would be asked to set up an authenticator before they can sign in
    /// again. Also the exposure of a first-comer bind: whoever holds such a password binds
    /// the first factor, unless an emailed code is asked for first.
    /// </summary>
    public int WithoutQualifyingFactor => ActiveUsers - WithQualifyingFactor;
}
