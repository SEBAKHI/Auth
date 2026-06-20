namespace Auth.Application.Configuration;

/// <summary>
/// Configuration settings for password policies and hashing.
/// </summary>
public class PasswordSettings
{
    public const string SectionName = "Password";

    /// <summary>
    /// Gets or sets the minimum password length.
    /// </summary>
    public int MinimumLength { get; set; } = 6;

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
    public int HistoryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the password expiration in days (0 = no expiration).
    /// </summary>
    public int ExpirationDays { get; set; } = 0;

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

    /// <summary>
    /// Gets or sets the server-side pepper (secret key) configuration for Argon2id hashing.
    /// </summary>
    public PepperSettings Pepper { get; set; } = new();

    /// <summary>
    /// Gets or sets the breached/weak password check configuration.
    /// </summary>
    public BreachedPasswordCheckSettings BreachedPasswordCheck { get; set; } = new();
}

/// <summary>
/// Configuration for the Argon2id server-side pepper (a secret key mixed into every hash via
/// <c>Argon2id.KnownSecret</c>). The pepper is stored in the secret store, never in the database,
/// so a database-only breach cannot brute-force the hashes without it.
/// <para>
/// <see cref="Enabled"/> is the operator toggle (appsettings). <see cref="CurrentKeyId"/> and
/// <see cref="Keys"/> are secret-managed material surfaced from the secret store under
/// <c>Password:Pepper:CurrentKeyId</c> and <c>Password:Pepper:Keys:{id}</c>.
/// </para>
/// </summary>
public class PepperSettings
{
    public const string SectionName = "Password:Pepper";

    /// <summary>
    /// Gets or sets whether peppering is enabled. When false the hasher behaves exactly as before
    /// (no <c>keyid</c> emitted) and ignores any configured pepper material.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the id of the pepper used to hash NEW passwords. 0 means "no current pepper".
    /// Surfaced from the secret store; not set in appsettings.
    /// </summary>
    public int CurrentKeyId { get; set; } = 0;

    /// <summary>
    /// Gets or sets the available peppers (base64) keyed by id. Older ids are retained so that
    /// previously-peppered hashes still verify until they are transparently rehashed on next login.
    /// Surfaced from the secret store; not set in appsettings.
    /// </summary>
    public Dictionary<int, string> Keys { get; set; } = new();
}

/// <summary>
/// Action taken when a candidate password is found in a breach corpus.
/// </summary>
public enum BreachAction
{
    /// <summary>Reject the password; the user must choose a different one.</summary>
    Enforce = 0,

    /// <summary>Allow the password but return a non-blocking warning.</summary>
    Warn = 1
}

/// <summary>
/// Configuration for checking candidate passwords against a known-breached corpus
/// (HIBP Pwned Passwords range API). Fully inert when <see cref="Enabled"/> is false.
/// </summary>
public class BreachedPasswordCheckSettings
{
    public const string SectionName = "Password:BreachedPasswordCheck";

    /// <summary>
    /// Gets or sets whether the breached-password check runs at all. When false no external call is
    /// made and the feature has no effect.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets what happens when a breached password is detected: reject (<see cref="BreachAction.Enforce"/>)
    /// or warn-and-allow (<see cref="BreachAction.Warn"/>).
    /// </summary>
    public BreachAction Mode { get; set; } = BreachAction.Enforce;

    /// <summary>
    /// Gets or sets whether to allow the password when the breach service is unavailable
    /// (timeout / error). True (default) avoids a self-inflicted outage; the event is logged.
    /// </summary>
    public bool FailOpen { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum number of times a password must appear in the breach corpus to be
    /// treated as breached. 1 = reject/warn on any appearance.
    /// </summary>
    public int RejectThreshold { get; set; } = 1;

    /// <summary>
    /// Gets or sets the per-request timeout in milliseconds for the breach service call.
    /// </summary>
    public int TimeoutMs { get; set; } = 2000;
}
