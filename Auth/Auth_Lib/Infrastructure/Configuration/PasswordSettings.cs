namespace Auth_Lib.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for password policies and hashing.
/// </summary>
public class PasswordSettings
{
    public const string SectionName = "Password";

    /// <summary>
    /// Gets or sets the minimum password length.
    /// </summary>
    public int MinimumLength { get; set; } = 12;

    /// <summary>
    /// Gets or sets whether uppercase letters are required.
    /// </summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>
    /// Gets or sets whether lowercase letters are required.
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// Gets or sets whether digits are required.
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// Gets or sets whether special characters are required.
    /// </summary>
    public bool RequireSpecialCharacter { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of previous passwords to check for reuse.
    /// </summary>
    public int HistoryCount { get; set; } = 5;

    /// <summary>
    /// Gets or sets the password expiration in days (0 = no expiration).
    /// </summary>
    public int ExpirationDays { get; set; } = 90;

    /// <summary>
    /// Gets or sets the maximum failed login attempts before lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the lockout duration in minutes.
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Gets the lockout duration as a TimeSpan.
    /// </summary>
    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutDurationMinutes);

    // Argon2id parameters (OWASP recommended for 2024)

    /// <summary>
    /// Gets or sets the Argon2id memory cost in KB.
    /// OWASP recommends 19456 KB (19 MiB) for Argon2id.
    /// </summary>
    public int Argon2MemorySize { get; set; } = 19456;

    /// <summary>
    /// Gets or sets the Argon2id iteration count.
    /// OWASP recommends 2 iterations for Argon2id.
    /// </summary>
    public int Argon2Iterations { get; set; } = 2;

    /// <summary>
    /// Gets or sets the Argon2id parallelism (threads).
    /// OWASP recommends 1 thread for Argon2id.
    /// </summary>
    public int Argon2Parallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets the salt size in bytes.
    /// </summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>
    /// Gets or sets the hash size in bytes.
    /// </summary>
    public int HashSize { get; set; } = 32;
}
