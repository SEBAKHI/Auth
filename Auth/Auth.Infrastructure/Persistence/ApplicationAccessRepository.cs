using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the application access gate.
/// </summary>
/// <remarks>
/// The access rule lives here once, as <see cref="EntitlementPredicateSql"/>,
/// and both the yes/no gate and the list form are built from that same text.
/// Two hand-written copies of an access rule drift, and the copy that drifts is
/// the one nobody is reading when it matters.
/// </remarks>
public class ApplicationAccessRepository : IApplicationAccessRepository
{
    /// <summary>
    /// THE access rule. An application admits a user when it is registered, not
    /// soft-deleted, switched on, and either open to everyone or holding a valid
    /// invitation for that user. Parameters: <c>@UserId</c>, <c>@Everyone</c>,
    /// and the application row aliased <c>a</c> in the enclosing query.
    /// </summary>
    private const string EntitlementPredicateSql = @"
        a.[IsDeleted] = 0
        AND a.[IsActive] = 1
        AND (
            a.[AccessMode] = @Everyone
            OR EXISTS (
                SELECT 1 FROM [dbo].[ApplicationUserAccess] aua
                WHERE aua.[ApplicationId] = a.[Id]
                  AND aua.[UserId] = @UserId
                  AND aua.[IsActive] = 1
                  AND aua.[RevokedAt] IS NULL
                  AND (aua.[ExpiresAt] IS NULL OR aua.[ExpiresAt] > GETUTCDATE()))
        )";

    private readonly IDbConnectionFactory _connectionFactory;

    public ApplicationAccessRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<bool> IsUserEntitledAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var match = await connection.ExecuteScalarAsync<int?>($@"
            SELECT TOP 1 1
            FROM [dbo].[Applications] a
            WHERE a.[Id] = @ApplicationId
              AND {EntitlementPredicateSql}",
            new
            {
                ApplicationId = applicationId,
                UserId = userId,
                Everyone = (byte)ApplicationAccessMode.Everyone
            });

        return match.HasValue;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserApplicationAccess>> GetApplicationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Same predicate, asked the other way round, so this list can never
        // disagree with what the sign-in gate will actually allow.
        var rows = await connection.QueryAsync<UserApplicationAccess>($@"
            SELECT
                a.[Id] AS [ApplicationId],
                a.[Code],
                a.[Name],
                a.[LogoUrl],
                a.[IsActive],
                CAST(CASE WHEN a.[AccessMode] = @Everyone THEN 1 ELSE 0 END AS BIT) AS [ViaOpenAccess],
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[ApplicationUserAccess] aua
                    WHERE aua.[ApplicationId] = a.[Id]
                      AND aua.[UserId] = @UserId
                      AND aua.[IsActive] = 1
                      AND aua.[RevokedAt] IS NULL
                      AND (aua.[ExpiresAt] IS NULL OR aua.[ExpiresAt] > GETUTCDATE()))
                    THEN 1 ELSE 0 END AS BIT) AS [ViaGrant]
            FROM [dbo].[Applications] a
            WHERE {EntitlementPredicateSql}
            ORDER BY a.[Name]",
            new
            {
                UserId = userId,
                Everyone = (byte)ApplicationAccessMode.Everyone
            });

        return rows.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationUserGrantRow>> GetGrantsAsync(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<ApplicationUserGrantRow>(@"
            SELECT
                u.[Id] AS [UserId],
                u.[Email],
                u.[FirstName],
                u.[LastName],
                u.[FullName] AS [DisplayName],
                u.[ProfileImageUrl],
                u.[Status],
                aua.[GrantedAt],
                aua.[GrantedBy],
                g.[FullName] AS [GrantedByName],
                aua.[ExpiresAt],
                aua.[Note]
            FROM [dbo].[ApplicationUserAccess] aua
            INNER JOIN [dbo].[Users] u ON aua.[UserId] = u.[Id]
            LEFT JOIN [dbo].[Users] g ON aua.[GrantedBy] = g.[Id]
            WHERE aua.[ApplicationId] = @ApplicationId
              AND aua.[IsActive] = 1
              AND aua.[RevokedAt] IS NULL
              AND (aua.[ExpiresAt] IS NULL OR aua.[ExpiresAt] > GETUTCDATE())
            ORDER BY aua.[GrantedAt] DESC",
            new { ApplicationId = applicationId });

        return rows.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ApplicationUserAccess?> GetGrantAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Deliberately unfiltered by state: the caller needs to tell "never
        // invited" from "invited then revoked", because the second must
        // reactivate this row rather than insert a duplicate the unique
        // constraint would reject anyway.
        var dto = await connection.QueryFirstOrDefaultAsync<GrantDto>(@"
            SELECT
                [Id], [ApplicationId], [UserId], [IsActive], [GrantedAt], [GrantedBy],
                [ExpiresAt], [RevokedAt], [RevokedBy], [Note],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[ApplicationUserAccess]
            WHERE [ApplicationId] = @ApplicationId AND [UserId] = @UserId",
            new { ApplicationId = applicationId, UserId = userId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The handler checks for an existing invitation before calling this, but
    /// two administrators inviting the same person at the same moment both pass
    /// that check and both insert. The unique (application, user) constraint
    /// stops the duplicate row; without catching it here the loser of the race
    /// gets a 500 for an operation whose intended outcome — that person is
    /// invited — already holds. Same idiom as the other upsert paths in this
    /// layer (UserKnownDeviceRepository, UserUiPreferenceRepository).
    /// </remarks>
    public async Task CreateGrantAsync(
        ApplicationUserAccess grant,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[ApplicationUserAccess] (
                [Id], [ApplicationId], [UserId], [IsActive], [GrantedAt], [GrantedBy],
                [ExpiresAt], [RevokedAt], [RevokedBy], [Note],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @ApplicationId, @UserId, @IsActive, @GrantedAt, @GrantedBy,
                @ExpiresAt, @RevokedAt, @RevokedBy, @Note,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                grant.Id,
                grant.ApplicationId,
                grant.UserId,
                grant.IsActive,
                grant.GrantedAt,
                grant.GrantedBy,
                grant.ExpiresAt,
                grant.RevokedAt,
                grant.RevokedBy,
                grant.Note,
                grant.CreatedAt,
                grant.CreatedBy,
                grant.ModifiedAt,
                grant.ModifiedBy
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent invitation won. Reinstate that row with this call's
            // terms so the last writer's expiry and note are the ones that
            // stand, exactly as they would have been without the race.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[ApplicationUserAccess] SET
                    [IsActive] = 1,
                    [GrantedAt] = @GrantedAt,
                    [GrantedBy] = @GrantedBy,
                    [ExpiresAt] = @ExpiresAt,
                    [RevokedAt] = NULL,
                    [RevokedBy] = NULL,
                    [Note] = @Note,
                    [ModifiedAt] = @GrantedAt,
                    [ModifiedBy] = @GrantedBy
                WHERE [ApplicationId] = @ApplicationId AND [UserId] = @UserId",
                new
                {
                    grant.ApplicationId,
                    grant.UserId,
                    grant.GrantedAt,
                    grant.GrantedBy,
                    grant.ExpiresAt,
                    grant.Note
                });
        }
    }

    /// <inheritdoc />
    public async Task UpdateGrantAsync(
        ApplicationUserAccess grant,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApplicationUserAccess] SET
                [IsActive] = @IsActive,
                [GrantedAt] = @GrantedAt,
                [GrantedBy] = @GrantedBy,
                [ExpiresAt] = @ExpiresAt,
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [Note] = @Note,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                grant.Id,
                grant.IsActive,
                grant.GrantedAt,
                grant.GrantedBy,
                grant.ExpiresAt,
                grant.RevokedAt,
                grant.RevokedBy,
                grant.Note,
                grant.ModifiedAt,
                grant.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveGrantAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var match = await connection.ExecuteScalarAsync<int?>(@"
            SELECT TOP 1 1
            FROM [dbo].[ApplicationUserAccess]
            WHERE [ApplicationId] = @ApplicationId
              AND [UserId] = @UserId
              AND [IsActive] = 1
              AND [RevokedAt] IS NULL
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { ApplicationId = applicationId, UserId = userId });

        return match.HasValue;
    }

    private record GrantDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid UserId { get; init; }
        public bool IsActive { get; init; }
        public DateTime GrantedAt { get; init; }
        public Guid GrantedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? Note { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public ApplicationUserAccess ToEntity() => new(
            Id,
            ApplicationId,
            UserId,
            IsActive,
            GrantedAt,
            GrantedBy,
            ExpiresAt,
            RevokedAt,
            RevokedBy,
            Note,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
