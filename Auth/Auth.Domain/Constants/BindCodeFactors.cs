namespace Auth.Domain.Constants;

/// <summary>
/// The values of the <c>Factor</c> variable of the
/// <see cref="NotificationTypeCodes.TwoFactorBindCode"/> email (S20, REVIEW 150 F11): which
/// first factor the emailed code finishes adding, so the message says what the code does.
/// Its templates branch on these exact strings, so they are declared once and never
/// localized.
/// </summary>
public static class BindCodeFactors
{
    /// <summary>
    /// An authenticator app: the code is entered with the app's code, and the message keeps
    /// version 1's words.
    /// </summary>
    public const string AuthenticatorApp = "authenticator-app";

    /// <summary>
    /// A passkey: the code lets the device that holds it sign in with its fingerprint, face or
    /// PIN, without the password, and the message says so.
    /// </summary>
    public const string Passkey = "passkey";
}
