namespace Auth.Domain.Constants;

/// <summary>
/// Bounds a second-factor code field shares where one field carries any of the
/// account's codes (the recovery of an account waiting for deletion).
/// </summary>
public static class TwoFactorCodeLimits
{
    /// <summary>
    /// Longest code such a field accepts: room for a recovery code as shown
    /// (<c>XXXX-XXXX</c>, nine characters) with spacing, and for a six-digit code.
    /// A ceiling on input size, not a format: each code's own check decides. Mirrored
    /// by the number in the TwoFactor.CodeTooLong sentence.
    /// </summary>
    public const int MaxLength = 16;
}
