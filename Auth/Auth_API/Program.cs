using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common.Filters;
using Auth_API.Common.HealthChecks;
using Auth_API.Common.Middleware;
using Auth_API.Tools;
using Auth.Application.Interfaces;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Authorization;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using Auth.Infrastructure.Email;
using Microsoft.Extensions.FileProviders;
using Auth.Infrastructure.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Auth.Shared.Diagnostics;
using Auth.Application.Features.Authentication.Common;
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

// Prevent JWT claim type mapping (e.g., "sub" -> ClaimTypes.NameIdentifier)
// This ensures we can access claims by their original JWT names
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// Configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<PasswordSettings>(builder.Configuration.GetSection(PasswordSettings.SectionName));
builder.Services.Configure<GatewaySettings>(builder.Configuration.GetSection(GatewaySettings.SectionName));
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection(SessionSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<ExternalAuthSettings>(builder.Configuration.GetSection(ExternalAuthSettings.SectionName));
builder.Services.Configure<ImageStorageSettings>(builder.Configuration.GetSection(ImageStorageSettings.SectionName));

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

var storageMode = AuthDataProtectionExtensions.ParseStorageMode(secretManagementSettings.StorageMode);

var dpCertificateSettings = builder.Configuration
    .GetSection(DataProtectionCertificateSettings.SectionName)
    .Get<DataProtectionCertificateSettings>() ?? new DataProtectionCertificateSettings();

// Data Protection key-ring location (encrypts the secrets file in Certificate/Dpapi modes).
// Defaults to %ProgramData%\AuthSystem\Keys; see AuthDataProtectionExtensions.ResolveKeyRingPath
// for why %LOCALAPPDATA% is unsafe under a service / IIS app-pool identity.
var dataProtectionKeyPath = AuthDataProtectionExtensions.ResolveKeyRingPath(
    builder.Configuration.GetValue<string>("DataProtection:KeyPath"));

builder.Services.AddDataProtection()
    .SetApplicationName("AuthSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .ConfigureKeyProtection(storageMode, dpCertificateSettings);

Log.Information("Secret storage mode: {Mode}", storageMode);

if (storageMode == SecretStorageMode.PlainText)
{
    // Secrets live as plain text in an appsettings file. Generate any missing values on first run.
    var plainTextResult = PlainTextSecretInitializer.EnsureSecrets(
        builder.Configuration,
        builder.Environment.ContentRootPath,
        secretManagementSettings.AutoGenerateKeys,
        secretManagementSettings.PlainTextTargetFile);

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
    }
}

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

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();
builder.Services.AddScoped<IUserSessionRepository, UserSessionRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
builder.Services.AddScoped<ITwoFactorAuthRepository, TwoFactorAuthRepository>();
builder.Services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IExternalAuthProviderRepository, ExternalAuthProviderRepository>();
builder.Services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
builder.Services.AddScoped<IWebhookKeyRepository, WebhookKeyRepository>();
builder.Services.AddScoped<IDashboardStatsRepository, DashboardStatsRepository>();

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
            secretManagementSettings.PlainTextTargetFile,
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

var jwtTokenService = new JwtTokenService(
    Options.Create(jwtSettings),
    passwordHasher,
    jwtDataProtectionProvider);
builder.Services.AddSingleton<IJwtTokenService>(jwtTokenService);

builder.Services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddSingleton<IRefreshTokenKeyService, RefreshTokenKeyService>();
builder.Services.AddSingleton<IWebhookKeyHasher, WebhookKeyHasher>();
builder.Services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
builder.Services.AddSingleton<IWebhookKeyGenerator, WebhookKeyGenerator>();
builder.Services.AddSingleton<ITotpService>(sp => new TotpService(sp.GetRequiredService<IPasswordHasher>()));
builder.Services.AddSingleton<IOtpGenerator, OtpGenerator>();
builder.Services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<IImageStorageService, FileSystemImageStorageService>();
builder.Services.AddSingleton<IImageUrlComposer, ImageUrlComposer>();
builder.Services.AddScoped<PasswordValidator>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

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
builder.Services.AddSingleton<IExternalAuthProviderFactory, ExternalAuthProviderFactory>();

// Integration Events
builder.Services.AddSingleton<Auth.Application.IntegrationEvents.IIntegrationEventPublisher,
    Auth.Infrastructure.IntegrationEvents.NoOpIntegrationEventPublisher>();

// Shared Application Services
builder.Services.AddScoped<ILoginResponseBuilder, LoginResponseBuilder>();
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
        IssuerSigningKey = jwtTokenService.GetSecurityKey()
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Support token from query string for WebSocket connections
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
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

    // Default policy
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100);
        opt.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60));
        opt.QueueLimit = builder.Configuration.GetValue("RateLimiting:QueueLimit", 10);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Stricter policy for login endpoint
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 5);
        opt.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:LoginWindowSeconds", 60));
        opt.QueueLimit = 0;
    });

    options.OnRejected = async (context, token) =>
    {
        var localizer = context.HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Localization.IStringLocalizer<Auth_Localization.Resources.Middleware.MiddlewareMessages>>();
        var message = localizer is not null && !localizer["Middleware.TooManyRequests"].ResourceNotFound
            ? localizer["Middleware.TooManyRequests"].Value
            : "Too many requests. Please try again later.";

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = message,
            retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? retryAfter.TotalSeconds
                : 60
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
    .AddSqlServer(connectionString, name: "database", tags: ["ready"])
    .AddTypeActivatedCheck<SigningKeyHealthCheck>(
        "signing-key",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

// CORS - configured per environment (OWASP A02: Security Misconfiguration)
var corsSettings = builder.Configuration.GetSection("Cors");
var allowedOrigins = corsSettings.GetSection("AllowedOrigins").Get<string[]>() ?? [];
var allowCredentials = corsSettings.GetValue("AllowCredentials", false);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0 && !allowedOrigins.Contains("*"))
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();

            if (allowCredentials)
                policy.AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment())
        {
            // Allow any origin in development only
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            throw new InvalidOperationException(
                "CORS AllowedOrigins must be explicitly configured in production. " +
                "Set Cors:AllowedOrigins in appsettings.json");
        }
    });
});

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

app.UseSerilogRequestLogging();

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
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
    }
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<JwtBlacklistValidationMiddleware>();
app.UseAuthorization();

// Health check endpoints (detailed JSON breakdown per check).
// Exception messages are included only in Development, or when HealthChecks:ExposeErrorDetails is
// explicitly enabled, because these endpoints are publicly reachable and could leak internal info.
var exposeHealthErrors = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("HealthChecks:ExposeErrorDetails", false);

Task WriteHealthResponse(HttpContext httpContext, HealthReport report)
{
    httpContext.Response.ContentType = "application/json; charset=utf-8";
    return httpContext.Response.WriteAsync(HealthCheckJsonFormatter.Serialize(report, exposeHealthErrors));
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

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
