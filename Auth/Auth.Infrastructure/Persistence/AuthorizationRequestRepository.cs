using System.Data.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Extensions.Logging;
using Auth.Domain.ValueObjects;

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
                [MaxAge], [Scope], [CreateOrganization], [CreatedAt], [ExpiresAt],
                [UiLocale], [UiTheme]
            ) VALUES (
                @Id, @HandleHash, @ApplicationId, @ClientId, @ResponseType,
                @RedirectUri, @CodeChallenge, @CodeChallengeMethod, @State, @Prompt,
                @MaxAge, @Scope, @CreateOrganization, @CreatedAt, @ExpiresAt,
                @UiLocale, @UiTheme
            )";

    /// <summary>
    /// Expired rows removed with every insert, so cleanup outpaces creation and
    /// the table stays near the requests that can still resume.
    /// </summary>
    internal const int PurgeOnInsert = 2;

    // Runs before every insert. Every sessionless authorize writes a row, and the
    // daily sweep stops at its per-run ceiling (200,000 rows by default), which a
    // handful of addresses at the gateway's 20 a minute already exceeds: without
    // this the table would outgrow the sweep under a spread-out flood. READPAST
    // skips rows another insert is purging, so two sign-ins never wait on each other.
    internal const string PurgeExpiredSql = @"
            DELETE TOP (@PurgeBatch) FROM [dbo].[AuthorizationRequests] WITH (READPAST)
            WHERE [ExpiresAt] < @Now";

    internal const string GetLiveByHandleHashSql = @"
            SELECT
                [Id], [HandleHash], [ApplicationId], [ClientId], [ResponseType],
                [RedirectUri], [CodeChallenge], [CodeChallengeMethod], [State], [Prompt],
                [MaxAge], [Scope], [CreateOrganization], [CreatedAt], [ExpiresAt],
                [UiLocale], [UiTheme]
            FROM [dbo].[AuthorizationRequests]
            WHERE [HandleHash] IN @Hashes
              AND [ExpiresAt] > @Now";

    internal const string ClearCreateOrganizationSql = @"
            UPDATE [dbo].[AuthorizationRequests] SET
                [CreateOrganization] = 0
            WHERE [Id] = @Id";

    internal const string CleanupExpiredSql = @"
            DELETE TOP (@BatchSize) FROM [dbo].[AuthorizationRequests]
            WHERE [ExpiresAt] < @OlderThan";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<AuthorizationRequestRepository> _logger;

    public AuthorizationRequestRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<AuthorizationRequestRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Housekeeping, best effort: a purge that times out or is chosen as a
        // deadlock victim (it shares the index with the daily sweep) must not fail
        // the sign-in it rides on. Only the database's own failure is caught, never
        // cancellation, and the insert below still fails loudly on its own.
        try
        {
            // Judged on the application's clock, as the resume and the sweep are.
            await connection.ExecuteAsync(
                PurgeExpiredSql, new { PurgeBatch = PurgeOnInsert, Now = request.CreatedAt });
        }
        catch (DbException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Purging expired pending authorize requests failed; the new request is stored anyway and the daily sweep removes them");
        }

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
            request.ExpiresAt,
            UiLocale = request.Hints.Language,
            UiTheme = request.Hints.Theme
        });
    }

    /// <inheritdoc />
    public async Task<AuthorizationRequest?> GetLiveByHandleHashAsync(
        CredentialHashCandidates candidates, DateTime nowUtc, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationRequestDto>(
            GetLiveByHandleHashSql, new { Hashes = candidates.ValuesFor(CredentialPurpose.AuthorizationRequestHandle), Now = nowUtc });

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
        public string? UiLocale { get; init; }
        public string? UiTheme { get; init; }

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
            ExpiresAt,
            // Read back through the same resolution as authorize: a stored value is
            // already resolved and reads as itself. A language this build no longer
            // supports reads as English, as authorize would show it; an unknown
            // colour mode reads as none. NULL reads as no hint.
            PresentationHints.From(UiLocale, UiTheme));
    }
}
