using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the WebAuthn challenge store (S20, PK6). A challenge is found
/// by every accepted fingerprint of its handle (<c>IN @Hashes</c>), so a key rotation never
/// strands one, and spent by one conditional statement.
/// </summary>
public sealed class WebAuthnChallengeRepository : IWebAuthnChallengeRepository
{
    // Spends a registration challenge in one statement, BEFORE the response is checked:
    // only an unused, unexpired registration challenge of the caller matches, so a second
    // request with the same handle, a challenge issued for an assertion, or another
    // account's handle spends nothing and finds nothing. Its options come back with it.
    internal const string ConsumeRegistrationSql = @"
            UPDATE [dbo].[WebAuthnChallenges] SET [UsedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[OptionsJson]
            WHERE [HandleHash] IN @Hashes
              AND [Purpose] = @Purpose
              AND [UserId] = @UserId
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > SYSUTCDATETIME()";

    internal const string InsertSql = @"
            INSERT INTO [dbo].[WebAuthnChallenges]
                ([Id], [HandleHash], [Purpose], [UserId], [OptionsJson], [ExpiresAt], [CreatedAt])
            VALUES (@Id, @HandleHash, @Purpose, @UserId, @OptionsJson, @ExpiresAt, @CreatedAt)";

    internal const string HasLiveRegistrationSql = @"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[WebAuthnChallenges]
                WHERE [UserId] = @UserId
                  AND [Purpose] = @Purpose
                  AND [UsedAt] IS NULL
                  AND [ExpiresAt] > SYSUTCDATETIME())
            THEN 1 ELSE 0 END AS BIT)";

    // The retention sweep: spent and unspent alike, once expired before the cutoff.
    internal const string CleanupSql = @"
            DELETE TOP (@BatchSize) FROM [dbo].[WebAuthnChallenges]
            WHERE [ExpiresAt] < @OlderThan";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasskeySchema _schema;

    public WebAuthnChallengeRepository(IDbConnectionFactory connectionFactory, IPasskeySchema schema)
    {
        _connectionFactory = connectionFactory;
        _schema = schema;
    }

    /// <inheritdoc />
    public async Task CreateAsync(WebAuthnChallenge challenge, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            throw new InvalidOperationException("The passkey schema is not deployed: no challenge can be stored.");
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                challenge.Id,
                challenge.HandleHash,
                Purpose = (byte)challenge.Purpose,
                challenge.UserId,
                challenge.OptionsJson,
                challenge.ExpiresAt,
                challenge.CreatedAt,
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<string?> TryConsumeRegistrationAsync(
        CredentialHashCandidates handleCandidates,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            ConsumeRegistrationSql,
            new
            {
                Hashes = handleCandidates.ValuesFor(CredentialPurpose.WebAuthnChallenge),
                Purpose = (byte)WebAuthnChallengePurpose.Registration,
                UserId = userId,
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> HasLiveRegistrationAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return false;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            HasLiveRegistrationSql,
            new { UserId = userId, Purpose = (byte)WebAuthnChallengePurpose.Registration },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return 0;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            CleanupSql,
            new { OlderThan = olderThanUtc, BatchSize = batchSize },
            cancellationToken: cancellationToken));
    }
}
