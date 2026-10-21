using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Auth.Domain.ValueObjects;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the OAuth authorization code repository.
/// </summary>
public class AuthorizationCodeRepository : IAuthorizationCodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    // The column widths of [UserAgent] and [DeviceId] in AuthorizationCodes.sql (OI-178).
    // Both are client-supplied, and cut to fit rather than allowed to fail the authorize
    // request that writes them.
    private const int UserAgentMaxLength = 500;
    private const int DeviceIdMaxLength = 64;

    public AuthorizationCodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode> CreateAsync(AuthorizationCode code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[AuthorizationCodes] (
                [Id], [ApplicationId], [UserId], [CodeHash], [RedirectUri],
                [CodeChallenge], [ExpiresAt], [ConsumedAt], [CreatedAt], [IpAddress],
                [Scope], [IdpSessionId], [RequiresClientAuthentication],
                [UserAgent], [DeviceId]
            ) VALUES (
                @Id, @ApplicationId, @UserId, @CodeHash, @RedirectUri,
                @CodeChallenge, @ExpiresAt, @ConsumedAt, @CreatedAt, @IpAddress,
                @Scope, @IdpSessionId, @RequiresClientAuthentication,
                @UserAgent, @DeviceId
            )",
            new
            {
                code.Id,
                code.ApplicationId,
                code.UserId,
                code.CodeHash,
                code.RedirectUri,
                code.CodeChallenge,
                code.ExpiresAt,
                code.ConsumedAt,
                code.CreatedAt,
                code.IpAddress,
                code.Scope,
                code.IdpSessionId,
                // 1 for a code issued to a server-side redirect URI, NULL otherwise (OI-171).
                RequiresClientAuthentication = code.RequiresClientAuthentication ? true : (bool?)null,
                // The browser that produced the code (OI-178).
                UserAgent = ColumnText.Truncate(code.UserAgent, UserAgentMaxLength),
                DeviceId = ColumnText.Truncate(code.DeviceId, DeviceIdMaxLength)
            });

        return code;
    }

    // Single atomic statement: only the caller that flips ConsumedAt gets the row back, so a
    // code can never be redeemed twice even under concurrent requests. Who may consume it is
    // part of the same statement (OI-171), so no read before it can go stale.
    private const string ConsumeOutputAndWhere = @"
            OUTPUT
                INSERTED.[Id], INSERTED.[ApplicationId], INSERTED.[UserId],
                INSERTED.[CodeHash], INSERTED.[RedirectUri], INSERTED.[CodeChallenge],
                INSERTED.[ExpiresAt], INSERTED.[ConsumedAt], INSERTED.[CreatedAt],
                INSERTED.[IpAddress], INSERTED.[IssuedSessionId], INSERTED.[Scope],
                INSERTED.[IdpSessionId], INSERTED.[RequiresClientAuthentication],
                INSERTED.[UserAgent], INSERTED.[DeviceId]
            WHERE [CodeHash] IN @Hashes
              AND [ConsumedAt] IS NULL";

    /// <summary>The public consume: a code issued to a server-side redirect URI is not consumable here.</summary>
    internal const string ConsumePublicStatement = @"
            UPDATE [dbo].[AuthorizationCodes] SET
                [ConsumedAt] = GETUTCDATE()" + ConsumeOutputAndWhere + @"
              AND [RequiresClientAuthentication] IS NULL";

    /// <summary>
    /// The authenticated consume: any code of the application that authenticated. It also marks
    /// the code as the server's, so a public code redeemed with the key is protected like a
    /// server-side one: only an authenticated replay of it may revoke the bound session it issued.
    /// </summary>
    internal const string ConsumeForClientStatement = @"
            UPDATE [dbo].[AuthorizationCodes] SET
                [ConsumedAt] = GETUTCDATE(),
                [RequiresClientAuthentication] = 1" + ConsumeOutputAndWhere + @"
              AND [ApplicationId] = @ApplicationId";

    /// <inheritdoc />
    public async Task<AuthorizationCode?> ConsumeByCodeHashAsync(CredentialHashCandidates candidates, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationCodeDto>(
            ConsumePublicStatement,
            new { Hashes = candidates.Values });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode?> ConsumeForClientAsync(
        CredentialHashCandidates candidates, Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationCodeDto>(
            ConsumeForClientStatement,
            new { Hashes = candidates.Values, ApplicationId = applicationId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode?> GetByCodeHashAsync(CredentialHashCandidates candidates, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationCodeDto>(@"
            SELECT
                [Id], [ApplicationId], [UserId], [CodeHash], [RedirectUri],
                [CodeChallenge], [ExpiresAt], [ConsumedAt], [CreatedAt], [IpAddress],
                [IssuedSessionId], [Scope], [IdpSessionId], [RequiresClientAuthentication],
                [UserAgent], [DeviceId]
            FROM [dbo].[AuthorizationCodes]
            WHERE [CodeHash] IN @Hashes",
            new { Hashes = candidates.Values });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[AuthorizationCodes]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    /// <inheritdoc />
    public async Task RecordIssuedSessionAsync(
        Guid codeId, Guid sessionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Written after the exchange succeeded, not during it: until tokens
        // actually exist there is nothing a replay would need to revoke, and a
        // failure here must never undo a sign-in that already completed.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[AuthorizationCodes] SET
                [IssuedSessionId] = @SessionId
            WHERE [Id] = @CodeId",
            new { CodeId = codeId, SessionId = sessionId });
    }

    // Internal DTO for mapping from database
    private record AuthorizationCodeDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid UserId { get; init; }
        public string CodeHash { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string CodeChallenge { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? ConsumedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? IpAddress { get; init; }
        public Guid? IssuedSessionId { get; init; }
        public string? Scope { get; init; }
        public Guid? IdpSessionId { get; init; }
        public bool? RequiresClientAuthentication { get; init; }
        public string? UserAgent { get; init; }
        public string? DeviceId { get; init; }

        public AuthorizationCode ToEntity() => new(
            Id,
            ApplicationId,
            UserId,
            CodeHash,
            RedirectUri,
            CodeChallenge,
            CreatedAt,
            ExpiresAt,
            ConsumedAt,
            IpAddress,
            IssuedSessionId,
            Scope,
            IdpSessionId,
            RequiresClientAuthentication == true,
            UserAgent,
            DeviceId);
    }
}
