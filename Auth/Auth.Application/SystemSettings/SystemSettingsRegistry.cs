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
    // Declared before Sections: static initializers run in declaration
    // order, so referencing this from the Sections initializer would
    // otherwise read null.
    private static readonly string[] LogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

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
                // ExpirationDays is deliberately absent: no login path evaluates
                // password age and nothing ever writes Users.PasswordExpiresUtc, so
                // offering it here promised a rotation policy that never ran.
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
            ]),

        new SettingSectionDefinition(
            Key: "Cors",
            ConfigRoot: "Cors",
            Group: SettingGroups.Access,
            Editable: true,
            Fields:
            [
                // Hot through DynamicCorsPolicyProvider (the policy is
                // rebuilt when the settings version changes).
                new SettingFieldDefinition("AllowedOrigins", SettingKind.StringArray),
                new SettingFieldDefinition("AllowCredentials", SettingKind.Bool)
            ]),

        new SettingSectionDefinition(
            Key: "RateLimiting",
            ConfigRoot: "RateLimiting",
            Group: SettingGroups.Access,
            Editable: true,
            // Hot via version-stamped partitions: existing per-IP windows
            // keep their old limits until they idle out; new partitions pick
            // up the saved values immediately. Defaults mirror the fallbacks
            // Program.cs passes to GetValue (no options class exists).
            // Only the limits an endpoint group is actually attached to appear
            // here. PermitLimit/WindowSeconds/QueueLimit are deliberately absent:
            // they fed a "fixed" policy no endpoint ever used, so the console was
            // offering three throttles that throttled nothing.
            Fields:
            [
                new SettingFieldDefinition("LoginPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 20),
                new SettingFieldDefinition("LoginWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                new SettingFieldDefinition("PasswordResetPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 10),
                new SettingFieldDefinition("PasswordResetWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60)
            ]),

        new SettingSectionDefinition(
            Key: "ExternalAuth",
            ConfigRoot: "ExternalAuth",
            Group: SettingGroups.Access,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("Google:Enabled", SettingKind.Bool, DefaultValue: false),
                new SettingFieldDefinition("Google:ClientId", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("Apple:Enabled", SettingKind.Bool, DefaultValue: false),
                new SettingFieldDefinition("Apple:ServicesId", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("Apple:TeamId", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("Apple:KeyId", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("Apple:PrivateKeyPem", SettingKind.String, Sensitive: true)
            ]),

        new SettingSectionDefinition(
            Key: "IdentityProvider",
            ConfigRoot: "IdentityProvider",
            Group: SettingGroups.Access,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("AccountsBaseUrl", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("PublicBaseUrl", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("AuthorizationCodeLifetimeSeconds", SettingKind.Int, Min: 10, Max: 300, DefaultValue: 60),
                // Hot-capable technically, but renaming orphans every
                // existing IdP session cookie — surfaced as restart-level.
                new SettingFieldDefinition("IdpSessionCookieName", SettingKind.String, RestartRequired: true, DefaultValue: "auth_idp"),
                new SettingFieldDefinition("IdpSessionLifetimeDays", SettingKind.Int, Min: 1, Max: 90, DefaultValue: 7)
            ]),

        new SettingSectionDefinition(
            Key: "Email",
            ConfigRoot: "Email",
            Group: SettingGroups.Communication,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("Enabled", SettingKind.Bool, DefaultValue: false),
                new SettingFieldDefinition("SmtpHost", SettingKind.String, DefaultValue: "localhost"),
                new SettingFieldDefinition("SmtpPort", SettingKind.Int, Min: 1, Max: 65535, DefaultValue: 587),
                new SettingFieldDefinition("UseSsl", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("Username", SettingKind.String),
                new SettingFieldDefinition("Password", SettingKind.String, Sensitive: true),
                new SettingFieldDefinition("SenderEmail", SettingKind.String, DefaultValue: "noreply@example.com"),
                new SettingFieldDefinition("SenderName", SettingKind.String, DefaultValue: "Auth System"),
                new SettingFieldDefinition("FrontendBaseUrl", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("OtpExpirationMinutes", SettingKind.Int, Min: 1, Max: 60, DefaultValue: 15),
                new SettingFieldDefinition("ResetTokenExpirationMinutes", SettingKind.Int, Min: 5, Max: 1440, DefaultValue: 30),
                new SettingFieldDefinition("RateLimitWindowSeconds", SettingKind.Int, Min: 10, Max: 3600, DefaultValue: 60),
                new SettingFieldDefinition("MaxOtpRequestsPerWindow", SettingKind.Int, Min: 1, Max: 20, DefaultValue: 3)
            ]),

        new SettingSectionDefinition(
            Key: "Notifications",
            ConfigRoot: "Notifications",
            Group: SettingGroups.Communication,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("UseOutbox", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("PollIntervalSeconds", SettingKind.Int, Min: 5, Max: 3600, DefaultValue: 30),
                new SettingFieldDefinition("BatchSize", SettingKind.Int, Min: 1, Max: 500, DefaultValue: 20),
                new SettingFieldDefinition("MaxAttempts", SettingKind.Int, Min: 1, Max: 20, DefaultValue: 5),
                new SettingFieldDefinition("StaleClaimMinutes", SettingKind.Int, Min: 1, Max: 120, DefaultValue: 5),
                new SettingFieldDefinition("NewDeviceAlertEnabled", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("NewDeviceAlertMinIntervalMinutes", SettingKind.Int, Min: 0, Max: 10080, DefaultValue: 60)
            ]),

        new SettingSectionDefinition(
            Key: "GeoIp",
            ConfigRoot: "GeoIp",
            Group: SettingGroups.Security,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("Enabled", SettingKind.Bool, RestartRequired: true, DefaultValue: false),
                // Both need a restart: the reader memory-maps the file once at
                // startup, so changing either at runtime would leave the loaded
                // database and the configured one describing different things.
                new SettingFieldDefinition("DatabasePath", SettingKind.String, RestartRequired: true, DefaultValue: "")
            ]),

        new SettingSectionDefinition(
            Key: "ImageStorage",
            ConfigRoot: "ImageStorage",
            Group: SettingGroups.Storage,
            Editable: true,
            Fields:
            [
                // Provider/PhysicalPath describe the server's disk layout;
                // RequestPath is baked into the static-files middleware.
                new SettingFieldDefinition("Provider", SettingKind.String, ReadOnly: true, DefaultValue: "filesystem"),
                new SettingFieldDefinition("PhysicalPath", SettingKind.String, ReadOnly: true, DefaultValue: "uploads/images"),
                new SettingFieldDefinition("PublicBaseUrl", SettingKind.String, DefaultValue: "/uploads/images"),
                // Stays editable — the PublicBaseUrl/RequestPath pairing rule
                // exists precisely so an operator can move the serving path —
                // but note the coupling: production reaches images through the
                // gateway, whose route table forwards /uploads/{**catch-all}
                // and whose ExemptPaths exempts /uploads/. Moving OUTSIDE that
                // prefix therefore needs a matching gateway route in the same
                // deployment; GatewayRouteCoverageTests fails when the two drift.
                new SettingFieldDefinition("RequestPath", SettingKind.String, RestartRequired: true, DefaultValue: "/uploads/images"),
                new SettingFieldDefinition("MaxSizeBytes", SettingKind.Int, Min: 1024, Max: 104857600, DefaultValue: 4194304),
                new SettingFieldDefinition("MaxMegapixels", SettingKind.Int, Min: 1, Max: 500, DefaultValue: 50),
                new SettingFieldDefinition("MaxEdgePx", SettingKind.Int, Min: 64, Max: 8192, DefaultValue: 1024),
                new SettingFieldDefinition("WebpQuality", SettingKind.Int, Min: 1, Max: 100, DefaultValue: 90),
                new SettingFieldDefinition(
                    "AllowedContentTypes",
                    SettingKind.StringArray,
                    DefaultValue: new[] { "image/png", "image/jpeg", "image/webp", "image/gif" })
            ]),

        // The three sections below share the AccountDeletion config root but
        // are separate ADMIN concerns: the deletion pipeline, the retention /
        // privacy commitments, and a one-shot maintenance switch. Section.Key
        // is the console + storage identity; ConfigRoot is where the values
        // actually live, so grouping by meaning costs nothing at the binder.
        new SettingSectionDefinition(
            Key: "AccountDeletion",
            ConfigRoot: "AccountDeletion",
            Group: SettingGroups.Operations,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("GraceDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 30),
                new SettingFieldDefinition("OtpExpirationMinutes", SettingKind.Int, Min: 1, Max: 60, DefaultValue: 15),
                new SettingFieldDefinition("WorkerPollMinutes", SettingKind.Int, Min: 1, Max: 1440, DefaultValue: 15),
                new SettingFieldDefinition("WorkerBatchSize", SettingKind.Int, Min: 1, Max: 500, DefaultValue: 25),
                // Floor of 2, not 1: at 1 the first transient failure — a brief
                // network blip while revoking an external token — permanently
                // dead-letters the deletion request into a state nothing retries
                // and no user can recover from. "Attempts" must mean at least
                // one retry, which is also what the field's own hint promises.
                new SettingFieldDefinition("MaxExecutionAttempts", SettingKind.Int, Min: 2, Max: 20, DefaultValue: 5),
                new SettingFieldDefinition("IdentifierHmacKeyPlain", SettingKind.String, Sensitive: true)
            ]),

        new SettingSectionDefinition(
            Key: "DataRetention",
            ConfigRoot: "AccountDeletion",
            Group: SettingGroups.Operations,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("PolicyVersion", SettingKind.String, DefaultValue: "2026.07"),
                new SettingFieldDefinition("LoginAttemptRetentionDays", SettingKind.Int, Min: 30, Max: 3650, DefaultValue: 365),
                new SettingFieldDefinition("OutboxRetentionDays", SettingKind.Int, Min: 30, Max: 3650, DefaultValue: 180),
                // Floor of 1095, not 30: the published privacy policy commits to
                // keeping the security record for at least three years, so the
                // console must not be able to shorten it below what users were
                // told. The Max is the ceiling the same policy owes them.
                new SettingFieldDefinition("AuditLogRetentionDays", SettingKind.Int, Min: 1095, Max: 3650, DefaultValue: 1095),
                // How long a destroyed e-mail stays blocked from re-registration.
                // The sweep raises whatever is set here to at least
                // AuditLogRetentionDays, so a shorter value cannot release an
                // address while records still keyed to it survive.
                new SettingFieldDefinition("IdentifierReservationDays", SettingKind.Int, Min: 1095, Max: 3650, DefaultValue: 1095)
            ]),

        // The legal identity published in the privacy policy. A settings section
        // rather than a code constant because the values differ per deployment
        // and are quoted verbatim in a document that must name the controller.
        // ConfigRoot is its own, not AccountDeletion: every full key must belong
        // to exactly one section (SystemSettingsRegistryTests enforces it).
        new SettingSectionDefinition(
            Key: "DataController",
            ConfigRoot: "DataController",
            Group: SettingGroups.Operations,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("LegalName", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("Address", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("PrivacyEmail", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("EmailProvider", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("HostingProvider", SettingKind.String, DefaultValue: ""),
                // Bare country name: the Turkish and French policy sentences
                // supply their own preposition and case suffix around it.
                new SettingFieldDefinition("HostingCountry", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("DpoContact", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("VerbisNo", SettingKind.String, DefaultValue: ""),
                new SettingFieldDefinition("KepAddress", SettingKind.String, DefaultValue: "")
            ]),

        new SettingSectionDefinition(
            Key: "Maintenance",
            ConfigRoot: "AccountDeletion",
            Group: SettingGroups.Operations,
            Editable: true,
            Fields:
            [
                // One-shot backfill evaluated by a hosted service at startup.
                new SettingFieldDefinition("RunEncryptionMigration", SettingKind.Bool, RestartRequired: true, DefaultValue: false)
            ]),

        new SettingSectionDefinition(
            Key: "HealthChecks",
            ConfigRoot: "HealthChecks",
            Group: SettingGroups.Operations,
            Editable: true,
            Fields:
            [
                new SettingFieldDefinition("ExposeErrorDetails", SettingKind.Bool, DefaultValue: false)
            ]),

        new SettingSectionDefinition(
            Key: "Serilog",
            ConfigRoot: "Serilog",
            Group: SettingGroups.Operations,
            Editable: true,
            // Levels are hot through LoggingLevelSwitchRegistry; sinks and
            // enrichers stay file-owned (they are built once at startup).
            Fields:
            [
                // Every switch — the default and all three overrides — is seeded
                // at Information (LoggingLevelSwitchRegistry), so an unset key
                // filters at Information rather than at "nothing". Stating that
                // keeps the console from showing a blank where a real level runs.
                new SettingFieldDefinition("MinimumLevel:Default", SettingKind.Enum, AllowedValues: LogLevels, DefaultValue: "Information"),
                new SettingFieldDefinition("MinimumLevel:Override:Microsoft", SettingKind.Enum, AllowedValues: LogLevels, DefaultValue: "Information"),
                new SettingFieldDefinition("MinimumLevel:Override:Microsoft.Hosting.Lifetime", SettingKind.Enum, AllowedValues: LogLevels, DefaultValue: "Information"),
                new SettingFieldDefinition("MinimumLevel:Override:System", SettingKind.Enum, AllowedValues: LogLevels, DefaultValue: "Information")
            ]),

        // Bootstrap sections: consumed before the database layer exists, so
        // they are read-only information cards in the console.
        new SettingSectionDefinition(
            Key: "DataProtection",
            ConfigRoot: "DataProtection",
            Group: SettingGroups.Infrastructure,
            Editable: false,
            Fields:
            [
                new SettingFieldDefinition("KeyPath", SettingKind.String, ReadOnly: true),
                new SettingFieldDefinition("Certificate:PfxPath", SettingKind.String, ReadOnly: true),
                new SettingFieldDefinition("Certificate:Thumbprint", SettingKind.String, ReadOnly: true),
                new SettingFieldDefinition("Certificate:PasswordEnvironmentVariable", SettingKind.String, ReadOnly: true)
            ]),

        new SettingSectionDefinition(
            Key: "SecretManagement",
            ConfigRoot: "SecretManagement",
            Group: SettingGroups.Infrastructure,
            Editable: false,
            Fields:
            [
                new SettingFieldDefinition("StorageMode", SettingKind.String, ReadOnly: true, DefaultValue: "PlainText"),
                new SettingFieldDefinition("SecretFilePath", SettingKind.String, ReadOnly: true),
                new SettingFieldDefinition("AutoGenerateKeys", SettingKind.Bool, ReadOnly: true, DefaultValue: true),
                new SettingFieldDefinition("EnableAdminApi", SettingKind.Bool, ReadOnly: true, DefaultValue: false)
                // RequiredPermission is deliberately absent: every SecretsController
                // endpoint carries [RequirePermission("secrets.manage")] as an
                // attribute, so the configuration key has no reader. Showing it as
                // "the permission that guards this API" was simply untrue — and
                // resolving authorization from a mutable string is not a fix worth
                // building.
            ]),

        new SettingSectionDefinition(
            Key: "ConnectionStrings",
            ConfigRoot: "ConnectionStrings",
            Group: SettingGroups.Infrastructure,
            Editable: false,
            // Credentials — never readable here; managed via files/secrets.
            Fields:
            [
                new SettingFieldDefinition("AuthDb", SettingKind.String, Sensitive: true)
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
