using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Persistence;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// One-time, config-gated encryption migration: upgrades TOTP secrets from
/// app-level Data Protection (or legacy plaintext) to per-user v2 ciphertext,
/// and encrypts plaintext phone numbers. Runs in-process at startup when
/// <c>AccountDeletion:RunEncryptionMigration</c> is true — the API already
/// holds the exact Data Protection key ring and connection the migration
/// needs, and the shared-hosting production environment offers no operator
/// shell for a console tool. Idempotent: only rows without the <c>v2:</c>
/// prefix are touched, so a second run reports zero changes. Dual-read keeps
/// concurrent traffic working throughout. Disable the flag after the report.
/// </summary>
public class EncryptionMigrationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountDeletionSettings _settings;
    private readonly ILogger<EncryptionMigrationService> _logger;

    public EncryptionMigrationService(
        IServiceScopeFactory scopeFactory,
        IOptions<AccountDeletionSettings> settings,
        ILogger<EncryptionMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.RunEncryptionMigration)
        {
            return;
        }

        _logger.LogWarning("One-time encryption migration starting (AccountDeletion:RunEncryptionMigration is enabled).");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            var perUserCrypto = scope.ServiceProvider.GetRequiredService<IPerUserCryptoService>();
            var secretProtector = scope.ServiceProvider.GetRequiredService<ITwoFactorSecretProtector>();

            using var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);

            // TOTP secrets: v1 app-level payloads (and pre-encryption plaintext
            // rows) are decoded by the protector's dual-read, then rewritten as
            // per-user v2 ciphertext.
            var totpRows = (await connection.QueryAsync<(Guid Id, Guid UserId, string SecretKey)>(
                "SELECT [Id], [UserId], [SecretKey] FROM [dbo].[TwoFactorAuth] WHERE [SecretKey] NOT LIKE 'v2:%'"))
                .ToList();

            int migratedSecrets = 0, skippedSecrets = 0;
            foreach (var row in totpRows)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var plaintext = await secretProtector.UnprotectAsync(row.UserId, row.SecretKey, stoppingToken);
                var upgraded = await secretProtector.ProtectAsync(row.UserId, plaintext, stoppingToken);

                // Optimistic guard: skip rows a concurrent write changed.
                var affected = await connection.ExecuteAsync(
                    "UPDATE [dbo].[TwoFactorAuth] SET [SecretKey] = @Upgraded WHERE [Id] = @Id AND [SecretKey] = @Original",
                    new { row.Id, Upgraded = upgraded, Original = row.SecretKey });
                if (affected == 1) { migratedSecrets++; } else { skippedSecrets++; }
            }

            // Phone numbers: plaintext values become per-user v2 ciphertext.
            var phoneRows = (await connection.QueryAsync<(Guid Id, string PhoneNumber)>(
                "SELECT [Id], [PhoneNumber] FROM [dbo].[Users] WHERE [PhoneNumber] IS NOT NULL AND [PhoneNumber] NOT LIKE 'v2:%'"))
                .ToList();

            int migratedPhones = 0, skippedPhones = 0;
            foreach (var row in phoneRows)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var encrypted = await perUserCrypto.EncryptAsync(
                    row.Id, row.PhoneNumber, EncryptedFieldPurpose.UserPhoneNumber, stoppingToken);

                var affected = await connection.ExecuteAsync(
                    "UPDATE [dbo].[Users] SET [PhoneNumber] = @Encrypted WHERE [Id] = @Id AND [PhoneNumber] = @Original",
                    new { row.Id, Encrypted = encrypted, Original = row.PhoneNumber });
                if (affected == 1) { migratedPhones++; } else { skippedPhones++; }
            }

            _logger.LogWarning(
                "Encryption migration complete: {MigratedSecrets} TOTP secrets and {MigratedPhones} phone numbers " +
                "re-encrypted under per-user keys ({SkippedSecrets}/{SkippedPhones} skipped by concurrent writes — " +
                "they re-migrate on the next run). Disable AccountDeletion:RunEncryptionMigration now.",
                migratedSecrets, migratedPhones, skippedSecrets, skippedPhones);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the migration resumes (idempotently) next start.
        }
        catch (Exception ex)
        {
            // Never take the API down over the migration: dual-read keeps
            // every un-migrated row working until the next attempt.
            _logger.LogError(ex, "Encryption migration failed; un-migrated rows keep working via dual-read. Re-run after fixing the cause.");
        }
    }
}
