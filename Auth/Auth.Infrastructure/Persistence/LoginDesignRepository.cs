using System.Data;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the sign-in page design repository.
/// </summary>
/// <remarks>
/// Every timestamp is sent as <c>datetime2</c>. Dapper's default for a <see cref="DateTime"/>
/// is the legacy <c>datetime</c>, which rounds to 3 ms: the stored revision would then differ
/// from the one the editor was given, and its next save would be refused as a conflict.
/// </remarks>
public class LoginDesignRepository : ILoginDesignRepository
{
    internal const string SelectColumns = @"
        [Id], [ApplicationId], [Name], [Content], [StringsJson], [ShowPlatformLogo], [IsActive],
        [ContentHash], [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]";

    /// <summary>The scope's active design other than the target: the one the swap replaces.</summary>
    internal const string FindActiveStatement = @"
        SELECT [Id] FROM [dbo].[LoginDesigns]
        WHERE {0} AND [IsActive] = 1 AND [Id] <> @Id";

    /// <summary>The first half of the swap: the scope's active design off.</summary>
    internal const string DeactivateScopeStatement = @"
        UPDATE [dbo].[LoginDesigns]
        SET [IsActive] = 0
        WHERE {0} AND [IsActive] = 1 AND [Id] <> @Id";

    /// <summary>
    /// The second half: the target on. A concurrent activation in the same scope that committed
    /// first makes this violate UX_LoginDesigns_ActivePerScope (2601), and the swap rolls back.
    /// </summary>
    internal const string ActivateTargetStatement = @"
        UPDATE [dbo].[LoginDesigns]
        SET [IsActive] = 1
        WHERE [Id] = @Id AND {0}";

    private readonly IDbConnectionFactory _connectionFactory;

    public LoginDesignRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>The scope predicate: the platform's rows, or one application's.</summary>
    private static string ScopeOf(Guid? applicationId) =>
        applicationId is null ? "[ApplicationId] IS NULL" : "[ApplicationId] = @ApplicationId";

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginDesign>> GetByScopeAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<LoginDesignRow>(new CommandDefinition($@"
            SELECT {SelectColumns}
            FROM [dbo].[LoginDesigns]
            WHERE {ScopeOf(applicationId)}
            ORDER BY [IsActive] DESC, [Name], [CreatedAt], [Id]",
            new { ApplicationId = applicationId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<LoginDesign?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<LoginDesignRow>(new CommandDefinition($@"
            SELECT {SelectColumns}
            FROM [dbo].[LoginDesigns]
            WHERE [Id] = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<LoginDesign?> GetActiveAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<LoginDesignRow>(new CommandDefinition($@"
            SELECT {SelectColumns}
            FROM [dbo].[LoginDesigns]
            WHERE {ScopeOf(applicationId)} AND [IsActive] = 1",
            new { ApplicationId = applicationId },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(LoginDesign design, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters(new
        {
            design.Id,
            design.ApplicationId,
            design.Name,
            design.Content,
            design.StringsJson,
            design.ShowPlatformLogo,
            design.IsActive,
            design.ContentHash,
            design.CreatedBy,
            design.ModifiedBy
        });
        parameters.Add("CreatedAt", design.CreatedAt, DbType.DateTime2);
        parameters.Add("ModifiedAt", design.ModifiedAt, DbType.DateTime2);

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO [dbo].[LoginDesigns]
                ([Id], [ApplicationId], [Name], [Content], [StringsJson], [ShowPlatformLogo], [IsActive],
                 [ContentHash], [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy])
            VALUES
                (@Id, @ApplicationId, @Name, @Content, @StringsJson, @ShowPlatformLogo, @IsActive,
                 @ContentHash, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)",
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(
        LoginDesign design,
        DateTime expectedModifiedAt,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters(new
        {
            design.Id,
            design.Name,
            design.Content,
            design.StringsJson,
            design.ShowPlatformLogo,
            design.ContentHash,
            design.ModifiedBy
        });
        parameters.Add("ModifiedAt", design.ModifiedAt, DbType.DateTime2);
        parameters.Add("ExpectedRevision", expectedModifiedAt, DbType.DateTime2);

        // IsActive is deliberately absent: activation has its own transaction, and a save built
        // from a copy read before an activation must not switch the design back.
        var affected = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[LoginDesigns]
            SET [Name] = @Name,
                [Content] = @Content,
                [StringsJson] = @StringsJson,
                [ShowPlatformLogo] = @ShowPlatformLogo,
                [ContentHash] = @ContentHash,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id
              AND COALESCE([ModifiedAt], [CreatedAt]) = @ExpectedRevision",
            parameters,
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<LoginDesignActivation> ActivateAsync(LoginDesign design, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var scope = ScopeOf(design.ApplicationId);
        var parameters = new { design.Id, design.ApplicationId };
        try
        {
            var previous = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
                string.Format(FindActiveStatement, scope), parameters, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                string.Format(DeactivateScopeStatement, scope), parameters, transaction, cancellationToken: cancellationToken));
            var activated = await connection.ExecuteAsync(new CommandDefinition(
                string.Format(ActivateTargetStatement, scope), parameters, transaction, cancellationToken: cancellationToken));

            if (activated != 1)
            {
                // Deleted since it was read: nothing changes, the previous design stays active.
                transaction.Rollback();
                return LoginDesignActivation.Conflict;
            }

            transaction.Commit();
            return new LoginDesignActivation(true, previous);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // UX_LoginDesigns_ActivePerScope: another activation in this scope committed first.
            transaction.Rollback();
            return LoginDesignActivation.Conflict;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateAsync(LoginDesign design, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[LoginDesigns]
            SET [IsActive] = 0
            WHERE [Id] = @Id AND [IsActive] = 1",
            new { design.Id },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteInactiveAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(@"
            DELETE FROM [dbo].[LoginDesigns]
            WHERE [Id] = @Id AND [IsActive] = 0",
            new { Id = id },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    private sealed record LoginDesignRow
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string StringsJson { get; init; } = "{}";
        public bool ShowPlatformLogo { get; init; }
        public bool IsActive { get; init; }
        public string ContentHash { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public LoginDesign ToEntity() => new(
            Id,
            ApplicationId,
            Name,
            Content,
            StringsJson,
            ShowPlatformLogo,
            IsActive,
            ContentHash,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
