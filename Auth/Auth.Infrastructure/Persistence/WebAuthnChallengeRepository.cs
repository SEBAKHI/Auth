using System.Data;
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

    // Spends a sign-in challenge in one statement, BEFORE the response is checked (S20 PR B):
    // only an unused, unexpired assertion challenge that names no account matches, so a second
    // request with the same handle, or a registration challenge's handle, spends nothing.
    internal const string ConsumeAssertionSql = @"
            UPDATE [dbo].[WebAuthnChallenges] SET [UsedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[OptionsJson]
            WHERE [HandleHash] IN @Hashes
              AND [Purpose] = @Purpose
              AND [UserId] IS NULL
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > SYSUTCDATETIME()";

    // The live sign-in challenge a page asks for again (T-B2): unspent, and with more than a
    // minute left by the database's own clock, never by when it was issued. It reads; it spends
    // nothing. The 60 is WebAuthnChallenge.ReuseMargin (pinned by the store's SQL-text tests).
    internal const string ReusableAssertionSql = @"
            SELECT [Id], [HandleHash], [Purpose], [UserId], [OptionsJson], [ExpiresAt], [UsedAt], [CreatedAt]
            FROM [dbo].[WebAuthnChallenges]
            WHERE [HandleHash] IN @Hashes
              AND [Purpose] = @Purpose
              AND [UserId] IS NULL
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > DATEADD(SECOND, 60, SYSUTCDATETIME())";

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

        var parameters = new DynamicParameters(new
        {
            challenge.Id,
            challenge.HandleHash,
            Purpose = (byte)challenge.Purpose,
            challenge.UserId,
            challenge.OptionsJson,
        });

        // DATETIME2, not Dapper's default DATETIME, which rounds to a 300th of a second: the
        // row then holds the exact expiry the options answered, and a page that asks again
        // (T-B2) is given that same expiresAt back.
        parameters.Add("ExpiresAt", challenge.ExpiresAt, DbType.DateTime2);
        parameters.Add("CreatedAt", challenge.CreatedAt, DbType.DateTime2);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));
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
    public async Task<string?> TryConsumeAssertionAsync(
        CredentialHashCandidates handleCandidates,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            ConsumeAssertionSql,
            new
            {
                Hashes = handleCandidates.ValuesFor(CredentialPurpose.WebAuthnChallenge),
                Purpose = (byte)WebAuthnChallengePurpose.Assertion,
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<WebAuthnChallenge?> GetReusableAssertionAsync(
        CredentialHashCandidates handleCandidates,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<WebAuthnChallengeDto>(new CommandDefinition(
            ReusableAssertionSql,
            new
            {
                Hashes = handleCandidates.ValuesFor(CredentialPurpose.WebAuthnChallenge),
                Purpose = (byte)WebAuthnChallengePurpose.Assertion,
            },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
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

    /// <summary>A challenge row as Dapper reads it.</summary>
    internal sealed record WebAuthnChallengeDto
    {
        public Guid Id { get; init; }
        public string HandleHash { get; init; } = string.Empty;
        public byte Purpose { get; init; }
        public Guid? UserId { get; init; }
        public string OptionsJson { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public DateTime CreatedAt { get; init; }

        public WebAuthnChallenge ToEntity() => new(
            Id, HandleHash, (WebAuthnChallengePurpose)Purpose, UserId, OptionsJson, ExpiresAt, UsedAt, CreatedAt);
    }
}
