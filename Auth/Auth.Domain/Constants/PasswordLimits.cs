namespace Auth.Domain.Constants;

/// <summary>
/// Bounds every password-bearing input shares.
/// </summary>
public static class PasswordLimits
{
    /// <summary>
    /// Longest password any endpoint accepts, whether it is being set or
    /// presented. A ceiling on input size, not a policy: PasswordValidator owns
    /// complexity and Password:MinimumLength owns the floor, whose registry
    /// maximum is pinned to this value so the floor can never exceed the
    /// ceiling. Without it the field accepted a request-body-sized string, and
    /// every byte was regex-scanned and then handed to Argon2id on anonymous
    /// endpoints. NIST 800-63B asks for at least 64; 128 leaves room for long
    /// passphrases. Mirrored by the number in Validation.Password.MaxLength.
    /// </summary>
    public const int MaxLength = 128;
}
