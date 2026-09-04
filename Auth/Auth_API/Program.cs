using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Auth.Domain.Entities;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Common.Filters;
using Auth_API.Common.HealthChecks;
using Auth_API.Common.Middleware;
using Auth_API.Modules.Media.Filters;
using Auth_API.Tools;
using Auth.Application.Interfaces;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Application.SystemSettings;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Authorization;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using Auth.Infrastructure.Notifications;
using Auth.Infrastructure.Notifications.Channels;
using Auth.Infrastructure.Notifications.Outbox;
using Microsoft.Extensions.FileProviders;
using Auth.Infrastructure.Configuration;
using Auth.Infrastructure.PrivacyPolicy;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Auth.Shared.Diagnostics;
using Auth.Shared.Http;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Application.Validators;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Auth_Localization.Extensions;
using Serilog;
using Serilog.Events;

// Prevent JWT claim type mapping (e.g., "sub" -> ClaimTypes.NameIdentifier)
// This ensures we can access claims by their original JWT names
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Machine-local overrides and generated secrets live in appsettings.{Environment}.local.json,
// which is git-ignored. The committed appsettings.{Environment}.json beside it carries only
// non-secret defaults. See LocalConfigurationExtensions for why the order matters.
builder.Configuration.AddEnvironmentLocalJsonFile(builder.Environment.EnvironmentName);

// Configure Serilog. Minimum levels route through LoggingLevelSwitchRegistry
// so they stay hot (system-settings saves apply immediately); sinks and
// enrichers stay file-owned and are built once here. A bootstrap logger is
// deliberately NOT used: the temp BuildServiceProvider below would freeze it
// before the real host build and abort startup.
var loggingLevelSwitches = new Auth_API.Common.Logging.LoggingLevelSwitchRegistry();
loggingLevelSwitches.ApplyFrom(builder.Configuration);

var serilogConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    // Declared AFTER ReadFrom.Configuration so the switches win control of
    // the default level and the known override namespaces.
    .MinimumLevel.ControlledBy(loggingLevelSwitches.Default);

foreach (var (overrideNamespace, levelSwitch) in loggingLevelSwitches.Overrides)
{
    serilogConfiguration.MinimumLevel.Override(overrideNamespace, levelSwitch);
}

Log.Logger = serilogConfiguration.CreateLogger();

builder.Host.UseSerilog();

// Configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<PasswordSettings>(builder.Configuration.GetSection(PasswordSettings.SectionName));
builder.Services.Configure<GatewaySettings>(builder.Configuration.GetSection(GatewaySettings.SectionName));
// Array settings need a post-bind pass: the binder APPENDS configured entries to a
// non-empty property initializer (making the initializer unremovable), and the
// database settings layer masks entries removed in the console with empty-string
// tombstones. Both would otherwise keep a removed entry alive at runtime while the
// console reports it gone. Runs on every rebind, so it also holds after a save.
builder.Services.PostConfigure<GatewaySettings>(SettingsArrayNormalizer.Apply);
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection(SessionSettings.SectionName));
builder.Services.Configure<RegistrationSettings>(builder.Configuration.GetSection(RegistrationSettings.SectionName));
builder.Services.Configure<OrganizationSettings>(builder.Configuration.GetSection(OrganizationSettings.SectionName));
builder.Services.Configure<IdentityProviderSettings>(builder.Configuration.GetSection(IdentityProviderSettings.SectionName));
// Password reset and email verification links are built from FrontendBaseUrl. An
// empty value silently yields a relative URL, i.e. a dead link in every email, so
// it is validated up front - but only when email is actually enabled, since it is
// off by default in development and CI.
builder.Services.AddOptions<EmailSettings>()
    .Bind(builder.Configuration.GetSection(EmailSettings.SectionName))
    .Validate(
        settings => !settings.Enabled || Uri.IsWellFormedUriString(settings.FrontendBaseUrl, UriKind.Absolute),
        "Email:FrontendBaseUrl must be an absolute URL when Email:Enabled is true.")
    .ValidateOnStart();
builder.Services.Configure<NotificationSettings>(builder.Configuration.GetSection(NotificationSettings.SectionName));
builder.Services.Configure<GeoIpSettings>(builder.Configuration.GetSection(GeoIpSettings.SectionName));
builder.Services.Configure<ExternalAuthSettings>(builder.Configuration.GetSection(ExternalAuthSettings.SectionName));
builder.Services.Configure<AccountDeletionSettings>(builder.Configuration.GetSection(AccountDeletionSettings.SectionName));
builder.Services.Configure<DataRetentionSettings>(builder.Configuration.GetSection(DataRetentionSettings.SectionName));
builder.Services.Configure<DataControllerSettings>(builder.Configuration.GetSection(DataControllerSettings.SectionName));
builder.Services.Configure<ImageStorageSettings>(builder.Configuration.GetSection(ImageStorageSettings.SectionName));
builder.Services.PostConfigure<ImageStorageSettings>(SettingsArrayNormalizer.Apply);
builder.Services.AddOptions<PrivacyPolicyPublicationSettings>()
    .Bind(builder.Configuration.GetSection(PrivacyPolicyPublicationSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.PhysicalPath),
        "PrivacyPolicyPublication:PhysicalPath must not be empty.")
    .ValidateOnStart();
// Scoped: it reads the image size limit through IOptionsSnapshot so a limit saved
// in the console governs the very next upload (see ImageUploadSizeLimitFilter).
builder.Services.AddScoped<ImageUploadSizeLimitFilter>();

// ════════════════════════════════════════════════════════════════════════════
// Secret Management - choose how the RSA signing key, HMAC key, and gateway token
// are stored and protected at rest (SecretManagement:StorageMode):
//   PlainText   -> stored directly in appsettings.Production.json (no encryption)
//   Certificate -> stored in an encrypted file; key ring protected by an X.509 certificate
//   Dpapi       -> stored in an encrypted file; key ring protected by Windows DPAPI (default)
// ════════════════════════════════════════════════════════════════════════════

// Load secret management settings from appsettings
var secretManagementSettings = builder.Configuration
    .GetSection(SecretManagementSettings.SectionName)
    .Get<SecretManagementSettings>() ?? new SecretManagementSettings();

var dpCertificateSettings = builder.Configuration
    .GetSection(DataProtectionCertificateSettings.SectionName)
    .Get<DataProtectionCertificateSettings>() ?? new DataProtectionCertificateSettings();

// Certificate is the shipped default so real deployments encrypt the key ring at rest, but a
// fresh clone has no certificate and would abort here. Development falls back to PlainText in
// that case; every other environment still fails fast. See ResolveStorageMode.
var configuredStorageMode = AuthDataProtectionExtensions.ParseStorageMode(secretManagementSettings.StorageMode);
var storageMode = AuthDataProtectionExtensions.ResolveStorageMode(
    secretManagementSettings.StorageMode,
    builder.Environment.IsDevelopment(),
    dpCertificateSettings);

// Generated plain-text secrets land in the running environment's own appsettings file unless
// one is configured explicitly, so a developer's keys never reach another environment's config.
var plainTextTargetFile = PlainTextSecretInitializer.ResolveTargetFile(
    secretManagementSettings.PlainTextTargetFile,
    builder.Environment.EnvironmentName);

// Data Protection key-ring location (encrypts the secrets file in Certificate/Dpapi modes).
// Defaults to %ProgramData%\AuthSystem\Keys; see AuthDataProtectionExtensions.ResolveKeyRingPath
// for why %LOCALAPPDATA% is unsafe under a service / IIS app-pool identity.
var dataProtectionKeyPath = AuthDataProtectionExtensions.ResolveKeyRingPath(
    builder.Configuration.GetValue<string>("DataProtection:KeyPath"));

// Fail fast if the crown-jewel secrets are sitting in plaintext in a Production
// config file. Runs BEFORE the DPAPI provider injects the decrypted key below,
// so it inspects the appsettings/env value, not the decrypted one.
StartupStep.Run(
    "production plaintext-secret check",
    () => ProductionSecretGuard.EnsureNoPlaintextSecrets(
        builder.Configuration, builder.Environment.IsProduction()));

// Set when this startup wrote a NEW permanent identifier HMAC key into an
// already-existing secrets file. Verified against the deletion registry once
// the connection string is resolved: a new key over a non-empty registry
// orphans every reservation, silently.
var identifierKeyWasMinted = false;

builder.Services.AddDataProtection()
    .SetApplicationName("AuthSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .ConfigureKeyProtection(storageMode, dpCertificateSettings);

Log.Information("Secret storage mode: {Mode}", storageMode);

if (storageMode != configuredStorageMode)
{
    Log.Warning(
        "SecretManagement:StorageMode is '{Configured}' but no DataProtection:Certificate is configured. " +
        "Falling back to {Effective} because this is the Development environment: secrets will be stored as " +
        "PLAIN TEXT in '{TargetFile}'. Configure DataProtection:Certificate (PfxPath or Thumbprint) to run " +
        "certificate mode locally. Non-Development environments fail to start instead of falling back.",
        configuredStorageMode, storageMode, plainTextTargetFile);
}

if (storageMode == SecretStorageMode.PlainText)
{
    // Secrets live as plain text in an appsettings file. Generate any missing values on first run.
    var plainTextResult = PlainTextSecretInitializer.EnsureSecrets(
        builder.Configuration,
        builder.Environment.ContentRootPath,
        secretManagementSettings.AutoGenerateKeys,
        plainTextTargetFile);

    if (plainTextResult.Generated)
    {
        // Inject into the running configuration so this process uses the new values immediately.
        builder.Configuration.AddInMemoryCollection(plainTextResult.ConfigValues);

        Log.Information("Generated plain-text secrets: {Keys}", string.Join(", ", plainTextResult.GeneratedKeys));

        if (plainTextResult.PublicKeyPem != null)
        {
            Log.Information("JWT Public Key (for external validation):\n{PublicKey}", plainTextResult.PublicKeyPem);
        }

        if (plainTextResult.PersistError != null)
        {
            Log.Warning(
                "Generated secrets are active for this run but were NOT saved to disk. They will be " +
                "regenerated on restart (invalidating existing tokens) until this is fixed. {Error}",
                plainTextResult.PersistError);
        }
    }
}
else
{
    // Certificate / Dpapi: secrets are stored in an encrypted file, decrypted via the key ring.
    var tempDpapiServices = new ServiceCollection();
    tempDpapiServices.AddDataProtection()
        .SetApplicationName("AuthSystem")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
        .ConfigureKeyProtection(storageMode, dpCertificateSettings);
    var tempDpapiProvider = tempDpapiServices.BuildServiceProvider();
    var dpapiProvider = tempDpapiProvider.GetRequiredService<IDataProtectionProvider>();

    // Add encrypted secrets to configuration (overrides appsettings.json values)
    Auth.Infrastructure.Configuration.SecretConfigurationExtensions.AddDpapiSecrets(
        builder.Configuration, dpapiProvider, secretManagementSettings.SecretFilePath);

    // Auto-generate keys on FIRST startup ONLY (when the secrets file does not yet exist).
    // After first startup the file exists, so keys are loaded and never regenerated.
    if (secretManagementSettings.AutoGenerateKeys && !File.Exists(secretManagementSettings.SecretFilePath))
    {
        Log.Information("First startup detected - auto-generating cryptographic keys...");

        var secretService = new DpapiSecretService(
            dpapiProvider,
            Options.Create(secretManagementSettings),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<DpapiSecretService>());

        var keyGenResult = secretService.GenerateMissingKeysAsync(CancellationToken.None).GetAwaiter().GetResult();

        Log.Information("Generated keys: {Keys}", string.Join(", ", keyGenResult.GeneratedKeys));

        if (keyGenResult.PublicKeyPem != null)
        {
            Log.Information("JWT Public Key (for external validation):\n{PublicKey}", keyGenResult.PublicKeyPem);
        }

        // Rebuild configuration to pick up newly generated secrets
        Auth.Infrastructure.Configuration.SecretConfigurationExtensions.AddDpapiSecrets(
            builder.Configuration, dpapiProvider, secretManagementSettings.SecretFilePath);
    }
    else if (File.Exists(secretManagementSettings.SecretFilePath))
    {
        Log.Debug("Loading existing secrets from {Path}", secretManagementSettings.SecretFilePath);

        // Top up ONLY secrets introduced after this file was created.
        //
        // The first-startup gate above stays deliberately. A blanket top-up
        // here would re-mint whichever of the JWT / refresh / gateway secrets
        // happened to read empty — a restored pre-fix backup, a renamed
        // property, a changed SecretFilePath — invalidating every issued token,
        // or leaving the API Gateway holding a token this process no longer
        // accepts: a total outage that repeats on every restart, logged at
        // Information. This path therefore provisions the late-added permanent
        // identifier key and nothing else, logs at Warning, and is verified
        // against the deletion registry once the database is reachable
        // (IdentifierKeyRegenerationGuard, below).
        if (secretManagementSettings.AutoGenerateKeys
            && string.IsNullOrWhiteSpace(builder.Configuration["AccountDeletion:IdentifierHmacKeyPlain"]))
        {
            var topUpService = new DpapiSecretService(
                dpapiProvider,
                Options.Create(secretManagementSettings),
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<DpapiSecretService>());

            var existingSecrets = topUpService.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult();
            existingSecrets.AccountDeletionIdentifierHmacKey =
                Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();
            topUpService.SaveSecretsAsync(existingSecrets, CancellationToken.None).GetAwaiter().GetResult();

            identifierKeyWasMinted = true;

            // Re-layer so this process uses the key it just wrote.
            Auth.Infrastructure.Configuration.SecretConfigurationExtensions.AddDpapiSecrets(
                builder.Configuration, dpapiProvider, secretManagementSettings.SecretFilePath);

            Log.Warning(
                "Provisioned the missing account-deletion identifier HMAC key into the existing secrets " +
                "file (fingerprint {Fingerprint}). This key is PERMANENT: back it up with the secrets file " +
                "and never rotate it. If this message appears on a later restart, the secrets file is not " +
                "persisting and identifier reservations are being silently orphaned.",
                IdentifierKeyRegenerationGuard.Fingerprint(existingSecrets.AccountDeletionIdentifierHmacKey));
        }
    }
}

// Every secret the process cannot serve a request without must exist NOW, not
// at the first request that happens to touch it. See RequiredSecretsGuard.
StartupStep.Run(
    "required-secrets check",
    () => RequiredSecretsGuard.EnsureAllPresent(builder.Configuration, storageMode.ToString()));

// Register SecretManagementSettings
builder.Services.Configure<SecretManagementSettings>(
    builder.Configuration.GetSection(SecretManagementSettings.SectionName));

// Register DpapiSecretService
builder.Services.AddSingleton<IDpapiSecretService>(sp =>
    new DpapiSecretService(
        sp.GetRequiredService<IDataProtectionProvider>(),
        sp.GetRequiredService<IOptions<SecretManagementSettings>>(),
        sp.GetRequiredService<ILogger<DpapiSecretService>>()));

// ════════════════════════════════════════════════════════════════════════════

// Database
var connectionString = builder.Configuration.GetConnectionString("AuthDb")
    ?? throw new InvalidOperationException("Connection string 'AuthDb' not found.");

// The null-check above cannot fire while appsettings.json ships a non-empty
// placeholder for this key, so the placeholder gets its own check.
StartupStep.Run(
    "connection-string resolution check",
    () => ConnectionStringGuard.EnsureResolved(connectionString));

// A permanent key minted over a populated registry is a silent data-integrity
// failure, so it is verified here — the first point where the database is
// reachable — and never merely trusted.
if (identifierKeyWasMinted)
{
    StartupStep.Run(
        "identifier-key regeneration check",
        () => IdentifierKeyRegenerationGuard.EnsureRegistryIsEmpty(connectionString));
}

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

// Probes a candidate connection string before the admin API commits it to the
// secrets file. Stateless, so a singleton.
builder.Services.AddSingleton<IConnectionStringProbe, SqlConnectionStringProbe>();

// ════════════════════════════════════════════════════════════════════════════
// Dynamic system settings: a database-backed configuration layer over the
// file/env layers (secret-owned keys are filtered on both the read and the
// write path, so the secret provider stays authoritative for them). The
// provider fails open — database down means file values, never a dead API.
// Escape hatch: AUTH_DISABLE_DB_SETTINGS=true skips the layer entirely so a
// bad override can always be bypassed and reset.
// ════════════════════════════════════════════════════════════════════════════
var disableDbSettings = string.Equals(
    Environment.GetEnvironmentVariable("AUTH_DISABLE_DB_SETTINGS"), "true", StringComparison.OrdinalIgnoreCase);

// Stamped into rate-limiter partition keys; bumps whenever a reload changed
// the loaded overrides so new partitions pick up new limits.
Func<int> settingsVersion = () => 0;

// Baseline snapshot BEFORE the database layer: what every field falls back
// to on reset (files/env). Captured here because files never change while
// the process runs.
var settingsBaseline = StartupValuesSnapshot.CaptureValues(builder.Configuration);

if (!disableDbSettings)
{
    var dbSettingsSource = new DbSettingsConfigurationSource(
        connectionString,
        DbSettingsConfigurationProvider.CaptureBaselineArrayLengths(builder.Configuration));
    ((IConfigurationBuilder)builder.Configuration).Add(dbSettingsSource);

    builder.Services.AddSingleton<ISystemSettingsReloader>(_ => dbSettingsSource.Provider!);
    builder.Services.AddHostedService<SystemSettingsRefreshService>();
    settingsVersion = () => dbSettingsSource.Provider?.Version ?? 0;
}
else
{
    Log.Warning("AUTH_DISABLE_DB_SETTINGS=true - database system-settings overrides are DISABLED for this run.");
    builder.Services.AddSingleton<ISystemSettingsReloader, NullSystemSettingsReloader>();
}

// Startup snapshot AFTER the database layer: what this process actually
// booted with — the reference for "pending restart" badges.
builder.Services.AddSingleton<IStartupValuesSnapshot>(
    new StartupValuesSnapshot(settingsBaseline, StartupValuesSnapshot.CaptureValues(builder.Configuration)));

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserEncryptionKeyRepository, UserEncryptionKeyRepository>();
builder.Services.AddScoped<IAccountDeletionRequestRepository, AccountDeletionRequestRepository>();
builder.Services.AddScoped<IAccountDeletionTombstoneRepository, AccountDeletionTombstoneRepository>();
builder.Services.AddScoped<IAccountDeletionVerificationRepository, AccountDeletionVerificationRepository>();
builder.Services.AddScoped<IPrivacyPolicyVersionRepository, PrivacyPolicyVersionRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IUploadedImageRepository, UploadedImageRepository>();
builder.Services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();
builder.Services.AddScoped<IUserSessionRepository, UserSessionRepository>();
builder.Services.AddScoped<IUserUiPreferenceRepository, UserUiPreferenceRepository>();
builder.Services.AddScoped<IUserKnownDeviceRepository, UserKnownDeviceRepository>();
// Singleton: the GeoLite2 reader memory-maps its file once and is thread-safe
// for reads, so opening it per request would only add I/O to the sign-in path.
builder.Services.AddSingleton<IGeoIpLookup, GeoIpLookup>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
// The single authority on "may this user sign in to this application?", asked by
// the authorize, token-exchange and refresh paths alike.
builder.Services.AddScoped<IApplicationAccessRepository, ApplicationAccessRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
builder.Services.AddScoped<IAuthorizationCodeRepository, AuthorizationCodeRepository>();
builder.Services.AddScoped<IIdpSessionRepository, IdpSessionRepository>();
builder.Services.AddScoped<IRevokedTokenStore, RevokedTokenStore>();
builder.Services.AddScoped<ITwoFactorAuthRepository, TwoFactorAuthRepository>();
builder.Services.AddScoped<ITwoFactorChallengeRepository, TwoFactorChallengeRepository>();
builder.Services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IOwnershipTransferCodeRepository, OwnershipTransferCodeRepository>();
builder.Services.AddScoped<IExternalAuthProviderRepository, ExternalAuthProviderRepository>();
builder.Services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
builder.Services.AddScoped<IWebhookKeyRepository, WebhookKeyRepository>();
builder.Services.AddScoped<IDashboardStatsRepository, DashboardStatsRepository>();
builder.Services.AddScoped<ISecretOperationChallengeRepository, SecretOperationChallengeRepository>();
builder.Services.AddScoped<ISecretRotationImpactRepository, SecretRotationImpactRepository>();
builder.Services.AddScoped<IPlatformSettingsRepository, PlatformSettingsRepository>();
builder.Services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
builder.Services.AddScoped<INotificationTypeRepository, NotificationTypeRepository>();
builder.Services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
builder.Services.AddScoped<INotificationLayoutRepository, NotificationLayoutRepository>();
builder.Services.AddScoped<INotificationOutboxRepository, NotificationOutboxRepository>();

// Domain Event Dispatcher
builder.Services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();

// ════════════════════════════════════════════════════════════════════════════
// Password pepper (Argon2id KnownSecret): ensure material exists when peppering is enabled.
// The pepper is required-when-enabled and safe to create on demand (it never invalidates tokens),
// so it is provisioned here independently of AutoGenerateKeys, covering fresh AND existing
// deployments. Running with an ephemeral pepper would lock out users on restart, so persistence
// failure is fatal.
// ════════════════════════════════════════════════════════════════════════════
if (builder.Configuration.GetValue<bool>("Password:Pepper:Enabled")
    && string.IsNullOrEmpty(builder.Configuration["Password:Pepper:CurrentKeyId"]))
{
    var pepperValue = Auth.Shared.Configuration.KeyMaterialGenerator.GeneratePepperBase64();
    var pepperConfig = new Dictionary<string, string?>
    {
        ["Password:Pepper:Keys:1"] = pepperValue,
        ["Password:Pepper:CurrentKeyId"] = "1"
    };

    if (storageMode == SecretStorageMode.PlainText)
    {
        var pepperPersistError = Auth.Shared.Configuration.PlainTextSecretInitializer.Persist(
            builder.Environment.ContentRootPath,
            plainTextTargetFile,
            pepperConfig);

        if (pepperPersistError != null)
        {
            throw new InvalidOperationException(
                $"Password peppering is enabled but the generated pepper could not be persisted: {pepperPersistError}. " +
                "Refusing to start with an ephemeral pepper (it would lock out all users on restart).");
        }
    }
    else
    {
        // Certificate / Dpapi: persist into the encrypted secret file using the same key ring.
        var tempPepperServices = new ServiceCollection();
        tempPepperServices.AddDataProtection()
            .SetApplicationName("AuthSystem")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
            .ConfigureKeyProtection(storageMode, dpCertificateSettings);
        var pepperDpProvider = tempPepperServices.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

        var pepperSecretService = new DpapiSecretService(
            pepperDpProvider,
            Options.Create(secretManagementSettings),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DpapiSecretService>.Instance);

        var pepperSecrets = pepperSecretService.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult();
        pepperSecrets.PasswordPeppers[1] = pepperValue;
        pepperSecrets.PasswordPepperCurrentKeyId = 1;
        pepperSecretService.SaveSecretsAsync(pepperSecrets, CancellationToken.None).GetAwaiter().GetResult();
    }

    builder.Configuration.AddInMemoryCollection(pepperConfig);
    Log.Information("Generated and persisted Argon2id password pepper (peppering enabled).");
}

// Services
// Create password hasher first (needed for JwtTokenService and TotpService)
var passwordSettings = builder.Configuration.GetSection(PasswordSettings.SectionName).Get<PasswordSettings>()
    ?? new PasswordSettings();
var passwordHasher = new Argon2PasswordHasher(Options.Create(passwordSettings));
builder.Services.AddSingleton<IPasswordHasher>(passwordHasher);

// Create JwtTokenService early so we can use its security key for JWT Bearer configuration
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? new JwtSettings();

// Build a temporary service provider to get IDataProtectionProvider for decrypting RSA keys
var tempServiceProvider = builder.Services.BuildServiceProvider();
var jwtDataProtectionProvider = tempServiceProvider.GetRequiredService<IDataProtectionProvider>();

// liveSettings makes token LIFETIMES hot (read per issue); issuer/audience/
// keys stay on the startup snapshot to match the validation parameters below.
var jwtTokenService = new JwtTokenService(
    Options.Create(jwtSettings),
    passwordHasher,
    jwtDataProtectionProvider,
    tempServiceProvider.GetRequiredService<IOptionsMonitor<JwtSettings>>());
builder.Services.AddSingleton<IJwtTokenService>(jwtTokenService);

// Token blacklist: one singleton behind both the interface and the concrete
// type (the background service needs the concrete LoadSnapshot for rehydration),
// with a write-behind channel and a durable-store background persister so
// revocations survive app-pool recycles.
builder.Services.AddSingleton(_ => Channel.CreateUnbounded<TokenRevocation>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<TokenRevocation>>().Writer);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<TokenRevocation>>().Reader);
builder.Services.AddSingleton<TokenBlacklistService>();
builder.Services.AddSingleton<ITokenBlacklistService>(sp => sp.GetRequiredService<TokenBlacklistService>());
builder.Services.AddHostedService<TokenRevocationBackgroundService>();
builder.Services.AddSingleton<IRefreshTokenKeyService, RefreshTokenKeyService>();
builder.Services.AddSingleton<IIdentifierHasher, IdentifierHasher>();
// Confirmation codes are keyed-hashed rather than password-hashed. Singleton
// alongside the key service it borrows: it holds no per-request state, and the
// password hasher it falls back to for codes minted before this shipped is a
// singleton too.
builder.Services.AddSingleton<IOtpHasher, HmacOtpHasher>();
// Scoped: the protector now rides the per-user crypto service (scoped DEK repo).
builder.Services.AddScoped<ITwoFactorSecretProtector, TwoFactorSecretProtector>();
builder.Services.AddScoped<IPerUserCryptoService, PerUserCryptoService>();
builder.Services.AddSingleton<IWebhookKeyHasher, WebhookKeyHasher>();
builder.Services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
builder.Services.AddSingleton<IWebhookKeyGenerator, WebhookKeyGenerator>();
builder.Services.AddSingleton<ITotpService>(sp => new TotpService(sp.GetRequiredService<IPasswordHasher>()));
builder.Services.AddSingleton<IOtpGenerator, OtpGenerator>();
builder.Services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
builder.Services.AddSingleton<IEnvironmentInfo, HostEnvironmentInfo>();
// Notification system: DB-managed templates rendered through Fluid, dispatched
// via channel strategies. Renderer/cache/factory are singletons (thread-safe,
// no per-request state); the service and rendering pipeline are scoped (repos).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ITemplateRenderer, FluidTemplateRenderer>();
builder.Services.AddSingleton<TemplateCache>();
builder.Services.AddSingleton<ITemplateCache>(sp => sp.GetRequiredService<TemplateCache>());
builder.Services.AddSingleton<ITemplateCacheInvalidator>(sp => sp.GetRequiredService<TemplateCache>());
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<IDirectEmailSender, DirectEmailSenderAdapter>();
builder.Services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddSingleton<INotificationChannelFactory, NotificationChannelFactory>();
builder.Services.AddScoped<INotificationRenderer, NotificationRenderingService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<INotificationDispatchSignal, NotificationDispatchSignal>();
builder.Services.AddHostedService<NotificationTemplateStartupCheck>();
builder.Services.AddHostedService<NotificationOutboxDispatcher>();
builder.Services.AddHostedService<EmailLogoRenditionStartupTask>();
builder.Services.AddSingleton<IImageStorageService, FileSystemImageStorageService>();
builder.Services.AddSingleton<IImageUrlComposer, ImageUrlComposer>();
// Privacy policy: rendered once when a revision is published. The file store
// writes the canonical Accounts-origin documents; the cache keeps the existing
// API read endpoint database-free. All three services are thread-safe singletons.
builder.Services.AddSingleton<IPolicyDocumentRenderer, PolicyDocumentRenderer>();
builder.Services.AddSingleton<IPolicyArtifactCache, PolicyArtifactCache>();
builder.Services.AddSingleton<IPolicyPublicationStore, FileSystemPolicyPublicationStore>();
builder.Services.AddScoped<PasswordValidator>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();
// Account deletion: the shared credential-kill primitive, the shared
// owned-organization and identifier-reservation rules, and the request /
// recovery / OTP pipelines used by every deletion flow.
builder.Services.AddScoped<ICredentialRevocationService, CredentialRevocationService>();
// The server's own signed memory of a prompt=login demand, so re-authentication
// is proved rather than assumed from the client having dropped the parameter.
builder.Services.AddSingleton<IStepUpTicketService, StepUpTicketService>();
// The one rule binding a provider sign-in's nonce to the browser it was issued
// to, shared by both anonymous endpoints that accept a provider ID token.
builder.Services.AddScoped<Auth.Application.Features.Authentication.Common.ExternalNonceGuard>();
builder.Services.AddScoped<Auth.Application.Features.Users.Common.OwnedOrganizationDeletionGuard>();
builder.Services.AddScoped<Auth.Application.Features.Users.Common.IdentifierReservationGuard>();
builder.Services.AddScoped<Auth.Application.Features.AccountDeletion.Common.AccountDeletionRequestor>();
builder.Services.AddScoped<Auth.Application.Features.AccountDeletion.Common.AccountDeletionRecoverer>();
// No principal may grant a permission it does not itself hold. Scoped, because
// it reads the actor's live permissions per request rather than trusting the
// token's claims, which outlive a revocation.
builder.Services.AddScoped<Auth.Application.Common.PermissionGrantGuard>();
builder.Services.AddScoped<Auth.Application.Common.OrganizationGrantGuard>();
builder.Services.AddScoped<Auth.Application.Features.AccountDeletion.Common.DeletionOtpService>();
// Step-up confirmation behind every destructive secret operation.
builder.Services.AddScoped<Auth.Application.Features.Secrets.Common.SecretOperationChallengeService>();
// One-shot, config-gated (AccountDeletion:RunEncryptionMigration) re-encryption
// of TOTP secrets and phone numbers under per-user DEKs.
builder.Services.AddHostedService<EncryptionMigrationService>();
// Grace-period executor + daily retention/destruction sweep.
builder.Services.AddHostedService<Auth.Infrastructure.AccountDeletion.AccountDeletionWorker>();
// Empties the tables that fill with rows nobody reads again: expired tokens,
// spent authorization codes, dead sessions. Every repository already had a
// cleanup method and none had a caller, so they had grown since day one.
builder.Services.AddHostedService<Auth.Infrastructure.Maintenance.ExpiredDataCleanupWorker>();

// Breached-password policy. Request-scoped warning sink + evaluator are always registered (cheap);
// the actual checker is HIBP only when enabled, otherwise a no-op with NO HttpClient registered.
builder.Services.AddScoped<IPasswordWarningContext, PasswordWarningContext>();
builder.Services.AddScoped<IPasswordBreachEvaluator, PasswordBreachEvaluator>();

var breachSettings = builder.Configuration
    .GetSection(BreachedPasswordCheckSettings.SectionName)
    .Get<BreachedPasswordCheckSettings>() ?? new BreachedPasswordCheckSettings();

if (breachSettings.Enabled)
{
    builder.Services.AddHttpClient<IBreachedPasswordChecker, HibpBreachedPasswordChecker>(client =>
    {
        client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
        client.Timeout = TimeSpan.FromMilliseconds(breachSettings.TimeoutMs);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AuthSystem-PwnedPasswords/1.0");
    });
}
else
{
    builder.Services.AddSingleton<IBreachedPasswordChecker, NullBreachedPasswordChecker>();
}

// External Authentication
builder.Services.AddSingleton<IExternalAuthProvider, GoogleAuthProvider>();
// Apple: id-token validation (JWKS over HTTP) + the token lifecycle used for
// deletion-time revocation. Registered once and forwarded into the strategy
// collections so the factory resolves them with no type switches.
builder.Services.AddHttpClient<AppleAuthProvider>();
builder.Services.AddSingleton<IExternalAuthProvider>(sp => sp.GetRequiredService<AppleAuthProvider>());
builder.Services.AddSingleton<AppleClientSecretGenerator>();
builder.Services.AddHttpClient<AppleTokenRevocationService>();
builder.Services.AddSingleton<IExternalTokenLifecycle>(sp => sp.GetRequiredService<AppleTokenRevocationService>());
builder.Services.AddSingleton<IExternalAuthProviderFactory, ExternalAuthProviderFactory>();
// Provider profile-picture import. The URL comes out of a provider-signed ID token
// rather than from user input, but it is still an outbound fetch on the sign-in
// path, so the handler refuses redirects, sends no cookies and does not decompress.
// Enabled/TimeoutMs/MaxBytes are read per call via IOptionsMonitor, so all three are
// hot; the time budget is applied per call rather than as HttpClient.Timeout for
// exactly that reason.
builder.Services.AddHttpClient<IExternalAvatarImporter, ExternalAvatarImporter>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AuthSystem-AvatarImport/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    AutomaticDecompression = System.Net.DecompressionMethods.None
});

// Integration Events
builder.Services.AddSingleton<Auth.Application.IntegrationEvents.IIntegrationEventPublisher,
    Auth.Infrastructure.IntegrationEvents.NoOpIntegrationEventPublisher>();

// Shared Application Services
builder.Services.AddScoped<ILoginResponseBuilder, LoginResponseBuilder>();
// Scopes a token's roles and permissions to the application it is minted for,
// on both the mint and the refresh path.
builder.Services.AddScoped<ITokenClaimsResolver, TokenClaimsResolver>();
builder.Services.AddScoped<ITwoFactorChallengeService, TwoFactorChallengeService>();
builder.Services.AddScoped<IPersonalOrganizationCreator, PersonalOrganizationCreator>();

// Authorization
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
builder.Services.AddHttpContextAccessor();

// MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.RegisterServicesFromAssemblyContaining<IPasswordHasher>();
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(Auth.Application.Behaviors.LoggingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(Auth.Application.Behaviors.ValidationBehavior<,>));
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddValidatorsFromAssemblyContaining<PasswordValidator>();

// Localization
builder.Services.AddAuthLocalization();

// Controllers with JSON options
builder.Services.AddControllers(options =>
    {
        // Surfaces non-blocking password warnings (Warn mode) as an X-Password-Warning response header.
        options.Filters.Add<PasswordWarningResultFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // DB timestamps are UTC but arrive as Kind.Unspecified from Dapper;
        // these converters guarantee offset-qualified ("Z") ISO-8601 output.
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
    });

// API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"),
        new QueryStringApiVersionReader("api-version"));
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Disable claim type mapping to preserve original JWT claim names
    options.MapInboundClaims = false;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = jwtSettings.ClockSkew,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        // Pin RS256: never accept a token signed under a different algorithm.
        ValidAlgorithms = ["RS256"],
        IssuerSigningKey = jwtTokenService.GetSecurityKey()
    };

    options.Events = new JwtBearerEvents
    {
        // No OnMessageReceived hook on purpose. Reading the token from a query string would
        // route it around JwtBlacklistValidationMiddleware, which keys on the Authorization
        // header and lets a request through untouched when that header is absent - so a
        // revoked, logged-out or locked-out token presented as ?access_token= would skip the
        // jti, sid and user-revocation checks alike. The comment this replaces cited WebSocket
        // support; there is no WebSocket, SignalR or hub anywhere in this solution, and the
        // uploads that are fetched by URL are served by UseStaticFiles with no authentication.
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.Headers.Append("Token-Expired", "true");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // All policies partition per client IP via ClientIpResolver (X-Forwarded-For),
    // never RemoteIpAddress: behind the gateway every request shares one internal
    // peer address (hairpin), which would collapse any per-client bucket into a
    // single global one. A global bucket on an auth path is collective punishment
    // and a self-inflicted DoS — never do it here.

    // Partition keys are stamped with the system-settings version: limits are
    // read live, but a partition caches its limiter on first hit — the stamp
    // makes changed limits take effect via fresh partitions while old ones
    // idle out.

    // There is deliberately NO general/default policy. A "fixed" policy once
    // existed here reading RateLimiting:PermitLimit/WindowSeconds/QueueLimit, but
    // no endpoint was ever attached to it and no GlobalLimiter was set, so those
    // three settings throttled nothing while the console offered them as live
    // controls. Attaching it globally is not the fix — see the note above on why a
    // shared bucket on an auth surface is a self-inflicted DoS. Limits are applied
    // per endpoint group below; a new group gets its own named policy plus its own
    // registry fields.

    // Interactive auth endpoints (login, register, external-login, verify-email,
    // forgot/reset-password submit, 2FA, invitation accept, token exchange) — per
    // client IP. One layer of a layered defense: per-IP throttling here plus
    // per-account lockout in the login handler (Password:MaxFailedAttempts) — a
    // single IP cannot brute force, a single account locks after N failures, and
    // legitimate users never share a bucket.
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"v{settingsVersion()}:{ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 20),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:LoginWindowSeconds", 60)),
                QueueLimit = 0
            }));

    // Registration, split out of the login policy above. Not a loosening of that
    // policy — a second bucket beside it: a named policy owns its own partitions,
    // so an IP spending its registration allowance still has its full login
    // allowance, and vice versa. That separation is the point. Registration
    // demand is an event (a launch, a campaign) while sign-in demand is a habit,
    // and while the two shared one bucket the only way to serve a registration
    // surge was to widen the bucket that also holds sign-in, token exchange,
    // account recovery and the deletion challenges — nineteen other endpoints.
    //
    // The number is a deployment fact, not a constant: what one IP may spend
    // here has to be read against how many client addresses the traffic arrives
    // from and what the hashing costs. This limit does not raise capacity; it
    // stops standing far below it. See the console hint, which carries the
    // arithmetic an operator needs to size it for their own deployment.
    //
    // The gateway has a matching "register" policy in front of this one. Both
    // must move together: every request passes the edge first, so this limit
    // alone is invisible.
    options.AddPolicy("register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"v{settingsVersion()}:{ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:RegisterPermitLimit", 200),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:RegisterWindowSeconds", 60)),
                QueueLimit = 0
            }));

    // Redeeming a reset token cannot be brute forced (the token carries 256 bits
    // of entropy), so this is hygiene for an anonymous endpoint rather than a
    // guessing defence — a stricter per-client bucket than the login policy.
    options.AddPolicy("password-reset", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"v{settingsVersion()}:{ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:PasswordResetPermitLimit", 10),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:PasswordResetWindowSeconds", 60)),
                QueueLimit = 0
            }));

    // Validating an API key is the most expensive thing an authenticated caller
    // can ask this process to do. The lookup narrows candidates by key prefix and
    // then runs a full Argon2id verify against each surviving row — 19 MiB and
    // two passes per verify, by design, because that cost is what makes a stolen
    // hash useless. Nothing bounded how many times it could be asked for, so a
    // caller holding apikeys:validate could turn a cheap request into hundreds of
    // megabytes of hashing work per second.
    //
    // Partitioned on the caller, not the client IP: the endpoint is authenticated,
    // relying parties legitimately call it from a small pool of server addresses,
    // and an IP bucket would let one busy integration throttle every other one
    // sharing a NAT. Falls back to IP only when the principal has no id to key on,
    // which should not happen behind [RequirePermission] but must not open the
    // bucket if it ever does.
    //
    // The claim is "sub", NOT ClaimTypes.NameIdentifier. This process clears
    // DefaultInboundClaimTypeMap and sets MapInboundClaims = false (see the top of
    // this file and the bearer registration below), precisely so claims keep their
    // JWT names — which means the SOAP-era ClaimTypes.NameIdentifier URI is never
    // present on any principal here. Reading it returned null on every request, so
    // this policy silently fell through to the IP fallback for its whole life: the
    // exact collapse the paragraph above says it exists to prevent. That the
    // limiter also has to run AFTER UseAuthentication for the principal to be
    // populated at all is the other half of the same defect; see the pipeline.
    options.AddPolicy("apikey-validate", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"v{settingsVersion()}:{httpContext.User.FindFirst(Auth.Domain.Constants.JwtClaimNames.Subject)?.Value
                ?? ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:ApiKeyValidatePermitLimit", 60),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:ApiKeyValidateWindowSeconds", 60)),
                QueueLimit = 0
            }));

    // Image upload is the one path that allocates in proportion to the request
    // rather than the response: every in-flight decode holds width*height*4
    // bytes (ImageStorage:MaxMegapixels x 4 MB) on the request thread until the
    // WebP is written. Every other policy here counts requests per window, and
    // a window cannot see simultaneity — the edge "api" policy admits a hundred
    // uploads a minute from one address, and nothing stopped all of them from
    // decoding at once. So this is a CONCURRENCY limiter, not a window, and it
    // is deliberately process-wide (a single partition): the resource it guards
    // is this process's memory, not a client's fair share. The short queue lets
    // a console user who drops three files at once wait a second or two instead
    // of seeing a 429 for the third; a queued request holds a connection, never
    // a bitmap. The version stamp keeps the limit hot: the old limiter finishes
    // its in-flight leases while the new one starts, which briefly allows both
    // budgets together — the accepted cost of not restarting.
    options.AddPolicy("image-upload", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: $"v{settingsVersion()}:image-upload",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:ImageUploadConcurrencyLimit", 2),
                QueueLimit = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // The gateway-exempt public surface: /health, /ready and the three
    // /.well-known discovery documents answer without the gateway token, so
    // they are the one place this process meets anonymous traffic it cannot
    // attribute — a direct caller writes X-Forwarded-For itself, and a
    // per-client partition here would hand every request a fresh bucket.
    // Hence a SINGLE process-wide partition and a concurrency ceiling rather
    // than a window: the number bounds how many of these requests are in
    // flight at once, which is what shields the thread pool and, behind
    // /ready, the cached single-flight database probe. Sixteen is generous
    // for any real probe cadence (monitors ask every ten to thirty seconds)
    // and is a constant rather than a setting because it sizes this process's
    // own headroom, not tenant demand. Rejections are 429 like every other
    // policy; a monitor that sees one during a flood is seeing the truth.
    // The version stamp is kept even though nothing here is read from settings
    // today: every named policy in this process is stamped, the guard test
    // enforces it, and it costs nothing — if the ceiling ever becomes a
    // setting, the hot-reload path is already in place.
    options.AddPolicy("public-surface", _ =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: $"v{settingsVersion()}:public-surface",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 16,
                QueueLimit = 0
            }));

    options.OnRejected = async (context, token) =>
    {
        var localizer = context.HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Localization.IStringLocalizer<Auth_Localization.Resources.Middleware.MiddlewareMessages>>();
        var message = localizer is not null && !localizer["Middleware.TooManyRequests"].ResourceNotFound
            ? localizer["Middleware.TooManyRequests"].Value
            : "Too many requests. Please try again later.";

        // Window policies attach RetryAfter; the image-upload and public-surface
        // concurrency policies cannot (a slot frees when work finishes, not on a
        // clock), so their rejections land on the fallback. A few seconds is the
        // honest hint there — sixty would tell a console user to wait a minute
        // for a queue that drains in one.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter.TotalSeconds
            : 5;

        // Name the allowance that ran out. Unambiguous in this process, and only
        // here: there is no GlobalLimiter in this host (see the note at the top
        // of this block), so the sole limiter that can refuse a request is the
        // named policy its endpoint opted into. A null policy would mean a
        // refusal arrived from a limiter no endpoint asked for, which is not a
        // state this configuration can reach — so it is logged as "unnamed"
        // rather than silently treated as one of the known buckets.
        RateLimitRejectionLog.Write(
            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth_API.RateLimiting"),
            context.HttpContext,
            RateLimitRejectionLog.PolicyOf(context.HttpContext) ?? "unnamed",
            ClientIpResolver.Resolve(context.HttpContext),
            retryAfterSeconds);

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter =
            ((int)retryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            // Not decoration. The SPA derives an error's KIND from this field and
            // from nothing else — getErrorStatus reads the body, never the
            // transport status — so while this body omitted it, a refusal here
            // was classified "unknown" and the user was told to contact support
            // instead of to wait a moment. The gateway's own 429 carries the
            // field and said the right thing, which is why the defect survived:
            // the two hosts refuse the same request for the same reason, and
            // whichever one gets there first has to say so identically.
            status = StatusCodes.Status429TooManyRequests,
            error = message,
            retryAfter = retryAfterSeconds
        }, token);
    };
});

// OpenAPI (.NET 10 native support)
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Auth API";
        document.Info.Version = "v1";
        document.Info.Description = "Enterprise Authentication System API";
        return Task.CompletedTask;
    });
});

// Health Checks
//   /health -> liveness  (tag "live") : is the process up? No external dependencies, so a
//                                       transient DB/secret outage never triggers a restart.
//   /ready  -> readiness (tag "ready"): can we actually serve auth requests? Database reachable
//                                       AND the JWT signing key is loaded.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Auth API process is running."), tags: ["live"])
    // Cached and single-flight, not the stock SQL Server check: /ready is
    // exempt from the gateway token, so it is where anonymous, unattributable
    // traffic can make this process open pooled connections. The stock check
    // opened one per request, and a burst of free GETs could hold the whole
    // pool for the probe's five-second timeout with sign-in starving behind
    // it. This one asks the database at most once per five seconds and lets
    // every concurrent caller share that answer; the timeout is unchanged.
    .AddTypeActivatedCheck<DatabaseReadinessHealthCheck>(
        "database",
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"],
        args: connectionString)
    .AddTypeActivatedCheck<SigningKeyHealthCheck>(
        "signing-key",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

// CORS - configured per environment (OWASP A02: Security Misconfiguration).
// The policy itself is served by DynamicCorsPolicyProvider from the LIVE
// configuration so saved origin changes apply without a restart; this
// startup check keeps the original production fail-fast.
var startupAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (startupAllowedOrigins.Length == 0 && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "CORS AllowedOrigins must be explicitly configured in production. " +
        "Set Cors:AllowedOrigins in appsettings.json");
}

builder.Services.AddCors();
builder.Services.AddSingleton<Microsoft.AspNetCore.Cors.Infrastructure.ICorsPolicyProvider, DynamicCorsPolicyProvider>();

// HSTS - only configure for production (OWASP A02: Security Misconfiguration)
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
        options.Preload = true;
    });
}

var app = builder.Build();

// Serilog levels: seed the switches from the effective configuration and
// keep them following configuration changes (system-settings saves reload
// the DB layer, which fires this token).
loggingLevelSwitches.ApplyFrom(app.Configuration);
Microsoft.Extensions.Primitives.ChangeToken.OnChange(
    () => ((IConfiguration)app.Configuration).GetReloadToken(),
    () => loggingLevelSwitches.ApplyFrom(app.Configuration));

// Configure the HTTP request pipeline

// HSTS - add Strict-Transport-Security header in production (OWASP A02)
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Forward headers from reverse proxy (OWASP A07: proper IP detection)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Security headers middleware (OWASP A02: Security Misconfiguration)
app.UseMiddleware<SecurityHeadersMiddleware>();

// RequestPath is rebuilt rather than taken verbatim: the invitation endpoints
// carry a bearer token as a path segment, and the default logger would write it
// into a file production keeps for ninety rolls. See SensitiveRoutePathRedactor
// for why the substitution is narrow rather than wholesale.
app.UseSerilogRequestLogging(options =>
{
    options.GetMessageTemplateProperties = (httpContext, requestPath, elapsedMs, statusCode) =>
    [
        new LogEventProperty("RequestMethod", new ScalarValue(httpContext.Request.Method)),
        new LogEventProperty("RequestPath", new ScalarValue(
            SensitiveRoutePathRedactor.Redact(httpContext, requestPath))),
        new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
        new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
    ];
});

// Localization middleware (must be before exception handling to set culture)
app.UseAuthLocalization();

// Exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Gateway token validation middleware
app.UseMiddleware<GatewayTokenValidationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Serve uploaded images (profile pictures, logos) from the configured storage directory.
// The directory lives outside the deploy tree; responses get nosniff + caching, and the
// request path is exempted from gateway-token validation (see Gateway:ExemptPaths).
var imageStorageSettings = app.Services.GetRequiredService<IOptions<ImageStorageSettings>>().Value;
var imageStorageRoot = Path.IsPathRooted(imageStorageSettings.PhysicalPath)
    ? imageStorageSettings.PhysicalPath
    : Path.Combine(AppContext.BaseDirectory, imageStorageSettings.PhysicalPath);
Directory.CreateDirectory(imageStorageRoot);

// Write-probe the uploads directory at startup: on IIS/Plesk the app-pool identity often lacks
// write access to a folder outside the deploy tree, which would otherwise only surface as
// failed uploads at runtime. Non-fatal — the rest of the API works without image uploads.
try
{
    var probePath = Path.Combine(imageStorageRoot, $".write-probe-{Guid.NewGuid():N}");
    File.Create(probePath).Dispose();
    File.Delete(probePath);
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    app.Logger.LogError(ex,
        "Image storage directory {Root} is not writable by the process identity; image uploads " +
        "WILL FAIL. Grant the app-pool identity Modify permission on ImageStorage:PhysicalPath.",
        imageStorageRoot);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageStorageRoot),
    RequestPath = imageStorageSettings.RequestPath,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Uploads are content-addressed by a random key, so they are immutable and cache
        // hard. The email logo renditions are the exception: they sit at a STABLE filename
        // that is overwritten whenever branding changes, precisely so that mail already
        // delivered picks up the new logo. A long TTL there would defeat the point, so they
        // get a short one and must be revalidated.
        var isEmailRendition = ctx.File.Name.StartsWith("platform-email-", StringComparison.OrdinalIgnoreCase);
        ctx.Context.Response.Headers["Cache-Control"] = isEmailRendition
            ? "public, max-age=3600, must-revalidate"
            : "public, max-age=86400";
    }
});

app.UseCors();
app.UseAuthentication();

// AFTER UseAuthentication, deliberately. A limiter placed ahead of it sees only
// the anonymous principal, so any policy that partitions on the caller silently
// degrades to its IP fallback — which is what "apikey-validate" did, turning a
// per-caller budget into a shared one and handing a single NAT the power to
// throttle every integration behind it. Still BEFORE UseAuthorization, because a
// request must be throttled before the endpoint runs, and before the blacklist
// check so a revoked token cannot spend an unbounded number of them.
//
// The anonymous policies ("login", "password-reset") are unaffected by the move:
// their endpoints carry no Authorization header, so the bearer handler is a no-op
// on them and they partition on the client IP exactly as before.
app.UseRateLimiter();

app.UseMiddleware<JwtBlacklistValidationMiddleware>();
app.UseAuthorization();

// Health check endpoints (detailed JSON breakdown per check).
// Exception messages are included only in Development, or when HealthChecks:ExposeErrorDetails is
// explicitly enabled, because these endpoints are publicly reachable and could leak internal info.
// Read per request so the system-settings toggle applies without a restart.
Task WriteHealthResponse(HttpContext httpContext, HealthReport report)
{
    var exposeHealthErrors = app.Environment.IsDevelopment()
        || app.Configuration.GetValue("HealthChecks:ExposeErrorDetails", false);
    httpContext.Response.ContentType = "application/json; charset=utf-8";
    return httpContext.Response.WriteAsync(HealthCheckJsonFormatter.Serialize(report, exposeHealthErrors));
}

// Both probes carry the public-surface concurrency ceiling (see the policy):
// they are exempt from the gateway token and answer anonymous callers directly.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
}).RequireRateLimiting("public-surface");
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
}).RequireRateLimiting("public-surface");

app.MapControllers();

// Handle command-line arguments for key generation
if (args.Contains("--generate-hmac-key"))
{
    var dataProtectionProvider = app.Services.GetRequiredService<IDataProtectionProvider>();
    var encryptedKey = KeyGeneratorTool.GenerateEncryptedHmacKey(dataProtectionProvider);

    Console.WriteLine();
    Console.WriteLine("=== HMAC Key Generated Successfully ===");
    Console.WriteLine();
    Console.WriteLine("Add this to your appsettings.json under the \"Jwt\" section:");
    Console.WriteLine();
    Console.WriteLine($"  \"RefreshTokenEncryptedKey\": \"{encryptedKey}\"");
    Console.WriteLine();
    Console.WriteLine("IMPORTANT: This key is encrypted using Windows DPAPI.");
    Console.WriteLine("It can only be decrypted on this machine (or machines sharing the Data Protection key ring).");
    Console.WriteLine();

    return;
}

if (args.Contains("--generate-rsa-key"))
{
    var dataProtectionProvider = app.Services.GetRequiredService<IDataProtectionProvider>();
    var (encryptedPrivateKey, publicKeyPem) = KeyGeneratorTool.GenerateEncryptedRsaKey(dataProtectionProvider);

    Console.WriteLine();
    Console.WriteLine("=== RSA Key Pair Generated Successfully ===");
    Console.WriteLine();
    Console.WriteLine("Add this to your appsettings.json under the \"Jwt\" section:");
    Console.WriteLine();
    Console.WriteLine($"  \"PrivateKeyEncrypted\": \"{encryptedPrivateKey}\"");
    Console.WriteLine();
    Console.WriteLine("Public Key (for external token validation):");
    Console.WriteLine(publicKeyPem);
    Console.WriteLine("IMPORTANT: The private key is encrypted using Windows DPAPI.");
    Console.WriteLine("It can only be decrypted on this machine (or machines sharing the Data Protection key ring).");
    Console.WriteLine();

    return;
}

try
{
    Log.Information("Starting Auth API...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
