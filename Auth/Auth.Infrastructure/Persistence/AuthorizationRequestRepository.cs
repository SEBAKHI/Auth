using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the pending authorize request repository (OI-145).
/// </summary>
/// <remarks>
/// The statements are constants so a test can run each one, as written, against
/// a throwaway database.
/// </remarks>
public class AuthorizationRequestRepository : IAuthorizationRequestRepository
{
    internal const string InsertSql = @"
            INSERT INTO [dbo].[AuthorizationRequests] (
                [Id], [HandleHash], [ApplicationId], [ClientId], [ResponseType],
                [RedirectUri], [CodeChallenge], [CodeChallengeMethod], [State], [Prompt],
                [MaxAge], [Scope], [CreateOrganization], [CreatedAt], [ExpiresAt]
            ) VALUES (
                @Id, @HandleHash, @ApplicationId, @ClientId, @ResponseType,
                @RedirectUri, @CodeChallenge, @CodeChallengeMethod, @State, @Prompt,
                @MaxAge, @Scope, @CreateOrganization, @CreatedAt, @ExpiresAt
            )";

    internal const string GetLiveByHandleHashSql = @"
            SELECT
                [Id], [HandleHash], [ApplicationId], [ClientId], [ResponseType],
                [RedirectUri], [CodeChallenge], [CodeChallengeMethod], [State], [Prompt],
                [MaxAge], [Scope], [CreateOrganization], [CreatedAt], [ExpiresAt]
            FROM [dbo].[AuthorizationRequests]
            WHERE [HandleHash] = @HandleHash
              AND [ExpiresAt] > @Now";

    internal const string ClearCreateOrganizationSql = @"
            UPDATE [dbo].[AuthorizationRequests] SET
                [CreateOrganization] = 0
            WHERE [Id] = @Id";

    internal const string CleanupExpiredSql = @"
            DELETE TOP (@BatchSize) FROM [dbo].[AuthorizationRequests]
            WHERE [ExpiresAt] < @OlderThan";

    private readonly IDbConnectionFactory _connectionFactory;

    public AuthorizationRequestRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task CreateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(InsertSql, new
        {
            request.Id,
            request.HandleHash,
            request.ApplicationId,
            request.ClientId,
            request.ResponseType,
            request.RedirectUri,
            request.CodeChallenge,
            request.CodeChallengeMethod,
            request.State,
            request.Prompt,
            request.MaxAge,
            request.Scope,
            request.CreateOrganization,
            request.CreatedAt,
            request.ExpiresAt
        });
    }

    /// <inheritdoc />
    public async Task<AuthorizationRequest?> GetLiveByHandleHashAsync(
        string handleHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationRequestDto>(
            GetLiveByHandleHashSql, new { HandleHash = handleHash, Now = nowUtc });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task ClearCreateOrganizationAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(ClearCreateOrganizationSql, new { Id = id });
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            CleanupExpiredSql, new { OlderThan = olderThanUtc, BatchSize = batchSize });
    }

    // Internal DTO for mapping from database
    private record AuthorizationRequestDto
    {
        public Guid Id { get; init; }
        public string HandleHash { get; init; } = string.Empty;
        public Guid ApplicationId { get; init; }
        public string ClientId { get; init; } = string.Empty;
        public string ResponseType { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string CodeChallenge { get; init; } = string.Empty;
        public string CodeChallengeMethod { get; init; } = string.Empty;
        public string? State { get; init; }
        public string? Prompt { get; init; }
        public int? MaxAge { get; init; }
        public string Scope { get; init; } = string.Empty;
        public bool CreateOrganization { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime ExpiresAt { get; init; }

        public AuthorizationRequest ToEntity() => new(
            Id,
            HandleHash,
            ApplicationId,
            ClientId,
            ResponseType,
            RedirectUri,
            CodeChallenge,
            CodeChallengeMethod,
            State,
            Prompt,
            MaxAge,
            Scope,
            CreateOrganization,
            CreatedAt,
            ExpiresAt);
    }
}
