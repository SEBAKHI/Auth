namespace Auth.Domain.Constants;

/// <summary>
/// Why an account's password was removed (OI-215): the <c>Reason</c> of
/// <see cref="Events.PasswordRemovedEvent"/>, recorded in the audit row and worded by
/// the notice. Declared once and never localized.
/// </summary>
public static class PasswordRemovalReasons
{
    /// <summary>
    /// The owner removed it from the profile, after a sign-in that proved it (D-215-3).
    /// </summary>
    public const string Owner = "owner";

    /// <summary>
    /// A sign-in by emailed code confirmed the account's address for the first time,
    /// and the password had been set before anyone proved that address (D-215-5 (a)):
    /// whoever typed it was never shown to hold the mailbox, so it does not survive
    /// the proof.
    /// </summary>
    public const string SetBeforeAddressProven = "set-before-address-proven";
}
