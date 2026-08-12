using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Authentication;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the login attempt repository.
/// </summary>
public class LoginAttemptRepository : ILoginAttemptRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LoginAttemptRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task CreateAsync(LoginAttempt attempt, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[LoginAttempts] (
                [Id], [UserId], [Username], [IsSuccessful], [FailureReason],
                [IpAddress], [UserAgent], [AttemptedAt], [ApplicationId], [TwoFactorChallengeId]
            ) VALUES (
                @Id, @UserId, @Username, @IsSuccessful, @FailureReason,
                @IpAddress, @UserAgent, @AttemptedAt, @ApplicationId, @TwoFactorChallengeId
            )",
            new
            {
                attempt.Id,
                attempt.UserId,
                Username = attempt.Email.Value, // Map Email property to Username column
                IsSuccessful = attempt.IsSuccess,
                attempt.FailureReason,
                attempt.IpAddress,
                attempt.UserAgent,
                attempt.AttemptedAt,
                attempt.ApplicationId,
                attempt.TwoFactorChallengeId
            });
    }

    /// <inheritdoc />
    public async Task ResolveTwoFactorCeremonyAsync(
        Guid challengeId,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The trailing predicate is the whole safety story: it matches only a row
        // still in the open state, so a retry, a duplicate verify, or a late
        // session-limit refusal cannot overwrite an outcome already recorded.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[LoginAttempts]
            SET [IsSuccessful] = @Succeeded,
                [FailureReason] = @FailureReason
            WHERE [TwoFactorChallengeId] = @ChallengeId
              AND [IsSuccessful] = 0
              AND [FailureReason] IS NULL",
            new
            {
                ChallengeId = challengeId,
                Succeeded = succeeded,
                FailureReason = succeeded ? null : failureReason
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignInHistoryEntry>> GetSignInHistoryAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The rejected-code count lives on the challenge, not on the attempt: the
        // ceremony row is written once at the start and the codes arrive after it.
        // LEFT JOIN so an attempt whose challenge has been purged still lists, with
        // a zero count, rather than vanishing from the user's own history.
        var rows = await connection.QueryAsync<SignInHistoryEntry>(@"
            SELECT TOP (@Count)
                la.[Id],
                la.[AttemptedAt],
                la.[IsSuccessful] AS [IsSuccess],
                la.[FailureReason],
                la.[IpAddress],
                la.[UserAgent],
                la.[TwoFactorChallengeId],
                ISNULL(ch.[AttemptCount], 0) AS [SecondFactorAttempts]
            FROM [dbo].[LoginAttempts] la
            LEFT JOIN [dbo].[TwoFactorChallenges] ch ON ch.[Id] = la.[TwoFactorChallengeId]
            WHERE la.[UserId] = @UserId
            ORDER BY la.[AttemptedAt] DESC",
            new { UserId = userId, Count = count });

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAttempt>> GetRecentByEmailAsync(
        string email,
        int count,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<LoginAttemptDto>(@"
            SELECT TOP (@Count)
                [Id], [UserId], [Username] AS [Email], [IsSuccessful] AS [IsSuccess],
                [FailureReason], [IpAddress], [UserAgent], [AttemptedAt], [ApplicationId]
            FROM [dbo].[LoginAttempts]
            WHERE [Username] = @Email
            ORDER BY [AttemptedAt] DESC",
            new { Email = email, Count = count });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAttempt>> GetRecentByIpAsync(
        string ipAddress,
        int count,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<LoginAttemptDto>(@"
            SELECT TOP (@Count)
                [Id], [UserId], [Username] AS [Email], [IsSuccessful] AS [IsSuccess],
                [FailureReason], [IpAddress], [UserAgent], [AttemptedAt], [ApplicationId]
            FROM [dbo].[LoginAttempts]
            WHERE [IpAddress] = @IpAddress
            ORDER BY [AttemptedAt] DESC",
            new { IpAddress = ipAddress, Count = count });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> CountFailedAttemptsAsync(
        string email,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var since = DateTime.UtcNow.Subtract(window);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[LoginAttempts]
            WHERE [Username] = @Email
              AND [IsSuccessful] = 0
              AND [AttemptedAt] >= @Since",
            new { Email = email, Since = since });
    }

    /// <inheritdoc />
    public async Task<int> CountFailedAttemptsByIpAsync(
        string ipAddress,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var since = DateTime.UtcNow.Subtract(window);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[LoginAttempts]
            WHERE [IpAddress] = @IpAddress
              AND [IsSuccessful] = 0
              AND [AttemptedAt] >= @Since",
            new { IpAddress = ipAddress, Since = since });
    }

    /// <inheritdoc />
    public async Task CleanupOldAttemptsAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[LoginAttempts] WHERE [AttemptedAt] < @OlderThan",
            new { OlderThan = olderThan });
    }

    private record LoginAttemptDto
    {
        public Guid Id { get; init; }
        public Guid? UserId { get; init; }
        public string Email { get; init; } = string.Empty;
        public bool IsSuccess { get; init; }
        public string? FailureReason { get; init; }
        public string? IpAddress { get; init; }
        public string? UserAgent { get; init; }
        public DateTime AttemptedAt { get; init; }
        public Guid? ApplicationId { get; init; }

        public LoginAttempt ToEntity() => new(
            Id,
            UserId,
            Email,
            IsSuccess,
            FailureReason,
            IpAddress,
            UserAgent,
            null, // Location not stored in DB
            AttemptedAt,
            ApplicationId);
    }
}
