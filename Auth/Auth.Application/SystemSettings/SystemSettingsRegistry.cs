using Auth.Domain.Constants;

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
                // Min is the ABSOLUTE FLOOR the whole password stack accepts,
                // not the recommendation: nothing between the sign-up form and
                // PasswordValidator carries a length rule of its own, so an
                // operator who sets 6 really gets 6. DefaultValue is the
                // recommended policy (OWASP/NIST 8) and mirrors both the
                // settings-class default and the shipped appsettings value, so
                // the console's "default" and a reset both land on the same
                // number. Before this, Min 8 forbade the default of 6 the
                // console displayed — a range that excluded its own default.
                // Max is pinned to the input ceiling every password field enforces,
                // so an operator can never require a minimum no password may reach.
                new SettingFieldDefinition("MinimumLength", SettingKind.Int, Min: 6, Max: PasswordLimits.MaxLength, DefaultValue: 8),
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
            // Only the four fields the code actually consumes. LifetimeHours,
            // ExtendOnActivity, ExtensionHours and IdleTimeoutMinutes are still
            // absent for the usual reason: nothing reads them, and a session's
            // real lifetime comes from Jwt:RefreshTokenLifetimeDays.
            //
            // MaxConcurrentSessions counts a user's live sessions across every
            // application, and defaults to 0 (unlimited) because that is what
            // the major providers ship — a cap is a control an operator turns
            // on, not a baseline. TerminateOldestOnMax picks what reaching it
            // does: end the least recently used sessions, or refuse the new
            // sign-in. Both are read per sign-in through IOptionsSnapshot, so
            // neither needs a restart. Max 100 is a sanity ceiling, not a
            // policy: past that a limit is not limiting anything.
            Fields:
            [
                new SettingFieldDefinition("MaxConcurrentSessions", SettingKind.Int, Min: 0, Max: 100, DefaultValue: 0),
                new SettingFieldDefinition("TerminateOldestOnMax", SettingKind.Bool, DefaultValue: true),
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
                // Registration has its own budget because it is the one endpoint
                // in the login family whose demand is an event rather than a
                // habit: a launch or a campaign asks for thousands of accounts in
                // an hour, and without a separate limit the only way to serve
                // that is to widen the bucket that also holds sign-in, token
                // exchange and account recovery. The default matches the others
                // in shape (a permit count over a window in seconds) and nothing
                // else — 200 is a working default for a five-egress deployment,
                // not a universal one; the console hint carries the arithmetic.
                new SettingFieldDefinition("RegisterPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 200),
                new SettingFieldDefinition("RegisterWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                new SettingFieldDefinition("PasswordResetPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 10),
                new SettingFieldDefinition("PasswordResetWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                new SettingFieldDefinition("ApiKeyValidatePermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 60),
                new SettingFieldDefinition("ApiKeyValidateWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                // Not a window: a ceiling on how many uploads may be DECODING at
                // once, process-wide. The upload path allocates width*height*4
                // bytes on the request thread for every in-flight decode, and a
                // per-IP request counter cannot see simultaneity — a hundred
                // uploads inside one minute are legal under the edge "api" policy
                // and nothing stopped all of them from decoding at the same
                // instant. Size it with ImageStorage:MaxMegapixels: permits x
                // megapixels x 4 MB is the memory the upload path may hold.
                new SettingFieldDefinition("ImageUploadConcurrencyLimit", SettingKind.Int, Min: 1, Max: 64, DefaultValue: 2)
            ]),

        new SettingSectionDefinition(
            Key: "GatewayRateLimiting",
            ConfigRoot: "GatewayRateLimiting",
            Group: SettingGroups.Access,
            Editable: true,
            // The API Gateway's OWN limiter, which runs at the edge before a
            // request ever reaches this process. A separate section from
            // RateLimiting above, not extra fields on it: these two throttles
            // live in two processes, and one storage row per section is what
            // makes "who owns this key" answerable.
            //
            // The gateway cannot read the database, so these values reach it
            // over the existing settings pull (GatewayRuntimeSettingsController
            // → GatewayRuntimeSettingsPoller). That makes them hot but not
            // instant: a save lands within one poll interval, and the gateway
            // stamps its partition keys with the settings version so a new
            // limit applies to fresh partitions rather than waiting for every
            // open window to idle out. The console copy says so; promising
            // "immediately" here would be the kind of half-truth this registry
            // exists to prevent.
            //
            // Defaults mirror the fallbacks API_Gateway/Program.cs passes to
            // GetValue and the values in its appsettings.json (no options class
            // exists on either side); GatewayRateLimitingParityTests guards the
            // three from drifting apart.
            Fields:
            [
                new SettingFieldDefinition("GlobalPermitLimit", SettingKind.Int, Min: 10, Max: 1000000, DefaultValue: 1000),
                new SettingFieldDefinition("GlobalWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                // 0 is a real choice here, not an unset sentinel: it means
                // reject on arrival instead of holding the request in a queue.
                new SettingFieldDefinition("GlobalQueueLimit", SettingKind.Int, Min: 0, Max: 10000, DefaultValue: 100),
                new SettingFieldDefinition("AuthPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 20),
                new SettingFieldDefinition("AuthWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                // The edge half of the registration split. Both halves have to
                // move together: the gateway policy runs first, so raising the
                // API's RegisterPermitLimit while this one stays at the auth
                // value changes nothing a client can observe — the request is
                // already refused a process earlier. Kept beside the auth pair
                // rather than appended, because the order here is the order the
                // console renders and the two belong side by side.
                new SettingFieldDefinition("RegisterPermitLimit", SettingKind.Int, Min: 1, Max: 10000, DefaultValue: 200),
                new SettingFieldDefinition("RegisterWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                new SettingFieldDefinition("ApiPermitLimit", SettingKind.Int, Min: 1, Max: 100000, DefaultValue: 100),
                new SettingFieldDefinition("ApiWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60),
                // 120, not the 10 this shipped with. Ten requests a minute was
                // stricter than the general api policy (100) for a route group
                // used by a handful of authenticated, permission-checked,
                // fully-audited accounts — and one console screen can spend
                // several requests, so an administrator hit the wall doing
                // ordinary work. Authorization and the audit log are what
                // defend /admin/**; a throttle sized for anonymous traffic
                // only defends it from its own operators.
                new SettingFieldDefinition("AdminPermitLimit", SettingKind.Int, Min: 1, Max: 100000, DefaultValue: 120),
                new SettingFieldDefinition("AdminWindowSeconds", SettingKind.Int, Min: 1, Max: 3600, DefaultValue: 60)
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
                new SettingFieldDefinition("Apple:PrivateKeyPem", SettingKind.String, Sensitive: true),
                // Read per sign-in through IOptionsMonitor, so none of the three
                // needs a restart.
                new SettingFieldDefinition("AvatarImport:Enabled", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("AvatarImport:TimeoutMs", SettingKind.Int, Min: 500, Max: 30000, DefaultValue: 3000),
                new SettingFieldDefinition("AvatarImport:MaxBytes", SettingKind.Int, Min: 65536, Max: 4194304, DefaultValue: 2097152),
                // The rollout switch for browser-bound nonces. Off by default so
                // deploying the server half cannot lock out every provider user
                // before the app half ships; turned on once the deployed app is
                // fetching its nonce from /auth/external-nonce. Read per sign-in,
                // so it takes effect — and can be reverted — without a restart.
                new SettingFieldDefinition("RequireNonce", SettingKind.Bool, DefaultValue: false)
            ]),

        new SettingSectionDefinition(
            // Sits beside the DataRetention section below, which keeps the
            // audit and login-attempt records. That one answers how long a
            // RECORD is owed to the user; this one answers how long a dead
            // CREDENTIAL row is still worth keeping as evidence. Different
            // question, different config root, so a separate section.
            Key: "ExpiredDataCleanup",
            ConfigRoot: "DataRetention",
            Group: SettingGroups.Operations,
            Editable: true,
            // How long a row that already fell out of use is kept. Not a
            // validity question - every row these govern is dead already. The
            // question is how long it stays USEFUL, and the use is detection:
            // a revoked refresh token is the only artifact that turns a stolen
            // token into a caught theft, and a consumed authorization code is
            // the only proof a code was replayed. Sweep either too early and
            // the attack still happens, silently, with nothing left to see it
            // by. Minimums are ALSO enforced in DataRetentionSettings, because
            // the console is not the only way a value gets set and a zero here
            // would put the cutoff at now.
            //
            // RefreshTokenDays floors at 90 in code whatever is entered: the
            // dashboard reports revocations over a trailing window the console
            // allows up to 90 days, so anything shorter makes those figures
            // quietly wrong instead of visibly absent.
            Fields:
            [
                new SettingFieldDefinition("Enabled", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("WorkerPollMinutes", SettingKind.Int, Min: 1, Max: 1440, DefaultValue: 15),
                new SettingFieldDefinition("BatchSize", SettingKind.Int, Min: 100, Max: 4000, DefaultValue: 4000),
                new SettingFieldDefinition("MaxRowsPerTablePerRun", SettingKind.Int, Min: 1000, Max: 5000000, DefaultValue: 200000),
                new SettingFieldDefinition("AuthorizationCodeDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 7),
                new SettingFieldDefinition("TwoFactorChallengeDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 7),
                new SettingFieldDefinition("PasswordResetTokenDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 7),
                new SettingFieldDefinition("EmailVerificationTokenDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 7),
                new SettingFieldDefinition("IdpSessionDays", SettingKind.Int, Min: 1, Max: 365, DefaultValue: 30),
                new SettingFieldDefinition("RefreshTokenDays", SettingKind.Int, Min: 90, Max: 730, DefaultValue: 90)
            ]),
        new SettingSectionDefinition(
            Key: "Registration",
            ConfigRoot: "Registration",
            Group: SettingGroups.Access,
            Editable: true,
            Fields:
            [
                // Both handlers read these through IOptionsSnapshot, so a save
                // takes effect on the next request — the point of a kill switch.
                // Both default to true: that is what every deployment did before
                // the switches existed, and an upgrade must not silently shut a
                // door the operator still wants open.
                new SettingFieldDefinition("AllowSelfRegistration", SettingKind.Bool, DefaultValue: true),
                new SettingFieldDefinition("AllowExternalProvisioning", SettingKind.Bool, DefaultValue: true),
                // The third door, and the one the console did not mention while
                // reporting the other two closed. Kept a separate switch because
                // an operator who shuts public sign-up has usually decided that
                // accounts arrive by invitation instead — folding the two
                // together would break the workflow the first choice implies.
                new SettingFieldDefinition("AllowInvitationRegistration", SettingKind.Bool, DefaultValue: true)
            ]),

        // Beside Registration rather than inside it. That section answers who may
        // create an ACCOUNT; this one answers who may create AUTHORITY — an
        // organization's creator becomes its owner, and the seeded owner role
        // carries the org:* family, invitation included. One screen answering both
        // questions is how an operator ends up believing a door is shut.
        new SettingSectionDefinition(
            Key: "Organizations",
            ConfigRoot: "Organizations",
            Group: SettingGroups.Access,
            Editable: true,
            Fields:
            [
                // Read through IOptionsSnapshot, so a save applies on the next
                // request. Defaults open: the endpoint has always been reachable
                // by any signed-in user, and an upgrade must not silently remove
                // a capability the accounts app still offers on its own page.
                new SettingFieldDefinition("AllowSelfServiceCreation", SettingKind.Bool, DefaultValue: true)
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
                new SettingFieldDefinition("MaxMegapixels", SettingKind.Int, Min: 1, Max: 500, DefaultValue: 24),
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
