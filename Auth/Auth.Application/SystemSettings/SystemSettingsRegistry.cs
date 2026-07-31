namespace Auth.Application.SystemSettings;

/// <summary>
/// The single source of truth for which appsettings sections and fields the
/// console can see and edit. The write path accepts ONLY paths defined here
/// as editable (whitelist), and the database configuration provider loads
/// ONLY keys defined here — anything else in storage is ignored.
/// <para>
/// RestartRequired mirrors how Program.cs actually consumes each value: a
/// field is hot only when every consumer re-reads it per request/operation
/// (IOptionsSnapshot / IOptionsMonitor / live IConfiguration reads).
/// </para>
/// </summary>
public static class SystemSettingsRegistry
{
    /// <summary>
    /// All sections, in the order the console presents them.
    /// </summary>
    public static IReadOnlyList<SettingSectionDefinition> Sections { get; } =
    [
        new SettingSectionDefinition(
            Key: "Jwt",
            ConfigRoot: "Jwt",
            Group: SettingGroups.Security,
            Editable: true,
            Fields:
            [
                // Issuer/Audience/KeyId/ClockSkew are baked into the JWT
                // bearer TokenValidationParameters at startup.
                new SettingFieldDefinition("Issuer", SettingKind.String, RestartRequired: true),
                new SettingFieldDefinition("Audience", SettingKind.String, RestartRequired: true),
                new SettingFieldDefinition("AccessTokenLifetimeMinutes", SettingKind.Int, Min: 1, Max: 1440, DefaultValue: 15),
                new SettingFieldDefinition("RefreshTokenLifetimeDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 7),
                new SettingFieldDefinition("KeyId", SettingKind.String, RestartRequired: true, DefaultValue: "auth-key-1"),
                new SettingFieldDefinition("RotateRefreshTokens", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("ClockSkewSeconds", SettingKind.Int, RestartRequired: true, Min: 0, Max: 300, DefaultValue: 60),
                new SettingFieldDefinition("PrivateKeyPath", SettingKind.String, Sensitive: true),
                new SettingFieldDefinition("PrivateKeyPem", SettingKind.String, Sensitive: true),
                new SettingFieldDefinition("PrivateKeyEncrypted", SettingKind.String, Sensitive: true),
                new SettingFieldDefinition("RefreshTokenEncryptedKey", SettingKind.String, Sensitive: true)
            ]),

        new SettingSectionDefinition(
            Key: "Password",
            ConfigRoot: "Password",
            Group: SettingGroups.Security,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("MinimumLength", SettingKind.Int, Min: 8, Max: 128, DefaultValue: 6),
                new SettingFieldDefinition("RequireUppercase", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("RequireLowercase", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("RequireDigit", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("RequireSpecialCharacter", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("HistoryCount", SettingKind.Int, Min: 0, Max: 24, DefaultValue: 3),
                new SettingFieldDefinition("ExpirationDays", SettingKind.Int, Min: 0, Max: 3650, DefaultValue: 0),
                new SettingFieldDefinition("MaxFailedAttempts", SettingKind.Int, Min: 1, Max: 100, DefaultValue: 5),
                new SettingFieldDefinition("LockoutDurationMinutes", SettingKind.Int, Min: 1, Max: 1440, DefaultValue: 15),
                // Argon2 parameters feed the singleton hasher built at
                // startup. Minimums are the OWASP Argon2id floor (m=19 MiB,
                // t=2, p=1); old hashes still verify after a change because
                // parameters are encoded in each stored hash.
                new SettingFieldDefinition("Argon2MemorySize", SettingKind.Int, RestartRequired: true, Min: 19456, Max: 1048576, DefaultValue: 19456),
                new SettingFieldDefinition("Argon2Iterations", SettingKind.Int, RestartRequired: true, Min: 2, Max: 10, DefaultValue: 2),
                new SettingFieldDefinition("Argon2Parallelism", SettingKind.Int, RestartRequired: true, Min: 1, Max: 16, DefaultValue: 1),
                new SettingFieldDefinition("SaltSize", SettingKind.Int, ReadOnly: true, DefaultValue: 16),
                new SettingFieldDefinition("HashSize", SettingKind.Int, ReadOnly: true, DefaultValue: 32),
                // Startup provisioning decides whether pepper key material
                // must exist, so the toggle is restart-bound.
                new SettingFieldDefinition("Pepper:Enabled", SettingKind.Bool, RestartRequired: true, DefaultValue: false),
                // Enabled + TimeoutMs shape the DI graph / HttpClient at
                // startup; Mode, FailOpen and RejectThreshold are evaluated
                // per check.
                new SettingFieldDefinition("BreachedPasswordCheck:Enabled", SettingKind.Bool, RestartRequired: true, DefaultValue: false),
                new SettingFieldDefinition("BreachedPasswordCheck:Mode", SettingKind.Enum, AllowedValues: ["Enforce", "Warn"], DefaultValue: "Enforce"),
                new SettingFieldDefinition("BreachedPasswordCheck:FailOpen", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("BreachedPasswordCheck:RejectThreshold", SettingKind.Int, Min: 1, Max: 1000, DefaultValue: 1),
                new SettingFieldDefinition("BreachedPasswordCheck:TimeoutMs", SettingKind.Int, RestartRequired: true, Min: 100, Max: 30000, DefaultValue: 2000)
            ]),

        new SettingSectionDefinition(
            Key: "Session",
            ConfigRoot: "Session",
            Group: SettingGroups.Security,
            Editable: true,
            // Only the two fields the code actually consumes. The other
            // Session:* keys in appsettings have no consumer (per-application
            // MaxConcurrentSessions lives on the Applications table), so
            // offering them here would be a lie.
            Fields:
            [
                new SettingFieldDefinition("TerminateSessionsOnPasswordChange", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("TerminateSessionsOnPasswordReset", SettingKind.Bool, DefaultValue: true)
            ]),

        new SettingSectionDefinition(
            Key: "Gateway",
            ConfigRoot: "Gateway",
            Group: SettingGroups.Security,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("ValidationEnabled", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition(
                    "ExemptPaths",
                    SettingKind.StringArray,
                    DefaultValue: new[] { "/.well-known/", "/health", "/ready" }),
                // The gateway process hardcodes the header name on its side;
                // changing it here alone would lock the API away.
                new SettingFieldDefinition("TokenHeaderName", SettingKind.String, ReadOnly: true, DefaultValue: "X-Gateway-Token"),
                new SettingFieldDefinition("ExpectedToken", SettingKind.String, Sensitive: true)
            ])
    ];

    private static readonly Dictionary<string, SettingSectionDefinition> ByKey =
        Sections.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks a section up by its key (case-insensitive).
    /// </summary>
    public static SettingSectionDefinition? TryGet(string sectionKey)
        => ByKey.GetValueOrDefault(sectionKey);

    /// <summary>
    /// Finds the field a flattened override path addresses. Array fields
    /// match their exact path only — index expansion happens later, in the
    /// configuration provider.
    /// </summary>
    public static SettingFieldDefinition? TryGetField(SettingSectionDefinition section, string fieldPath)
        => section.Fields.FirstOrDefault(f => string.Equals(f.Path, fieldPath, StringComparison.OrdinalIgnoreCase));
}
