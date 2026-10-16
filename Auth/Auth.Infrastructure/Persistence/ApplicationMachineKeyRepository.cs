using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of <see cref="IApplicationMachineKeyRepository"/> (OI-128).
/// </summary>
public class ApplicationMachineKeyRepository : IApplicationMachineKeyRepository
{
    private const string Columns = @"
        k.[Id], k.[ApplicationId], k.[Kid], k.[Name], k.[Algorithm], k.[PublicKeyJwk],
        k.[CreatedAt], k.[CreatedBy], k.[LastUsedAt], k.[RevokedAt], k.[RevokedBy], k.[RevokeReason], k.[Purpose]";

    /// <summary>
    /// The cap and the insert in one statement. The count reads the application's active keys of
    /// the new key's purpose (OI-171: the cap is per purpose) under an update lock held to the end of the statement (HOLDLOCK makes it a range lock), so
    /// a second creation for the same application waits, then counts the first one's row. Without
    /// it, two concurrent creations at two active keys both count two and both insert.
    /// </summary>
    internal const string InsertUnderCap = @"
        INSERT INTO [dbo].[ApplicationMachineKeys] (
            [Id], [ApplicationId], [Kid], [Name], [Algorithm], [PublicKeyJwk], [CreatedAt], [CreatedBy], [Purpose]
        )
        SELECT @Id, @ApplicationId, @Kid, @Name, @Algorithm, @PublicKeyJwk, @CreatedAt, @CreatedBy, @Purpose
        WHERE (
            SELECT COUNT(*) FROM [dbo].[ApplicationMachineKeys] WITH (UPDLOCK, HOLDLOCK)
            WHERE [ApplicationId] = @ApplicationId AND [RevokedAt] IS NULL AND [Purpose] = @Purpose
        ) < @MaxActiveKeys";

    private readonly IDbConnectionFactory _connectionFactory;

    public ApplicationMachineKeyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationMachineKey>> ListByApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<MachineKeyRow>(new CommandDefinition($@"
            SELECT {Columns}
            FROM [dbo].[ApplicationMachineKeys] k
            WHERE k.[ApplicationId] = @ApplicationId
            ORDER BY k.[CreatedAt] DESC, k.[Id]",
            new { ApplicationId = applicationId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<ApplicationMachineKey?> GetAsync(Guid applicationId, Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<MachineKeyRow>(new CommandDefinition($@"
            SELECT {Columns}
            FROM [dbo].[ApplicationMachineKeys] k
            WHERE k.[Id] = @Id AND k.[ApplicationId] = @ApplicationId",
            new { Id = id, ApplicationId = applicationId },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<MachineKeyCreateOutcome> TryCreateAsync(
        ApplicationMachineKey key, int maxActiveKeys, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        try
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                InsertUnderCap,
                new
                {
                    key.Id,
                    key.ApplicationId,
                    key.Kid,
                    key.Name,
                    key.Algorithm,
                    key.PublicKeyJwk,
                    key.CreatedAt,
                    key.CreatedBy,
                    Purpose = (byte)key.Purpose,
                    MaxActiveKeys = maxActiveKeys,
                },
                cancellationToken: cancellationToken));

            return inserted == 1 ? MachineKeyCreateOutcome.Created : MachineKeyCreateOutcome.LimitReached;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // UX_ApplicationMachineKeys_Kid: this public key is already registered.
            return MachineKeyCreateOutcome.DuplicateKey;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRevokeAsync(ApplicationMachineKey key, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[ApplicationMachineKeys] SET
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [RevokeReason] = @RevokeReason
            WHERE [Id] = @Id AND [ApplicationId] = @ApplicationId AND [RevokedAt] IS NULL",
            new { key.Id, key.ApplicationId, key.RevokedAt, key.RevokedBy, key.RevokeReason },
            cancellationToken: cancellationToken));

        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<MachineKeyCredential?> GetActiveByKidAsync(string kid, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // A key authenticates only while its application is alive: the same join that keeps keys
        // of inactive or soft-deleted applications from validating (ApiKeyRepository).
        var row = await connection.QuerySingleOrDefaultAsync<MachineKeyRow>(new CommandDefinition($@"
            SELECT {Columns}, a.[Code] AS [ApplicationCode]
            FROM [dbo].[ApplicationMachineKeys] k
            INNER JOIN [dbo].[Applications] a ON a.[Id] = k.[ApplicationId]
            WHERE k.[Kid] = @Kid AND k.[RevokedAt] IS NULL
              AND a.[IsActive] = 1 AND a.[IsDeleted] = 0",
            new { Kid = new DbString { Value = kid, Length = 64, IsAnsi = false } },
            cancellationToken: cancellationToken));

        return row is null ? null : new MachineKeyCredential(row.ToEntity(), row.ApplicationCode);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[ApplicationMachineKeys] SET [LastUsedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    private sealed record MachineKeyRow
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public string Kid { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string Algorithm { get; init; } = string.Empty;
        public string PublicKeyJwk { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? LastUsedAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? RevokeReason { get; init; }
        public byte Purpose { get; init; } = (byte)MachineKeyPurpose.Machine;
        public string ApplicationCode { get; init; } = string.Empty;

        public ApplicationMachineKey ToEntity() => new(
            Id,
            ApplicationId,
            Kid,
            Name,
            Algorithm,
            PublicKeyJwk,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            CreatedBy,
            LastUsedAt is { } used ? DateTime.SpecifyKind(used, DateTimeKind.Utc) : null,
            RevokedAt is { } revoked ? DateTime.SpecifyKind(revoked, DateTimeKind.Utc) : null,
            RevokedBy,
            RevokeReason,
            (MachineKeyPurpose)Purpose);
    }
}
