using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
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
                [IpAddress], [UserAgent], [AttemptedAt], [ApplicationId]
            ) VALUES (
                @Id, @UserId, @Username, @IsSuccessful, @FailureReason,
                @IpAddress, @UserAgent, @AttemptedAt, @ApplicationId
            )",
            new
            {
                attempt.Id,
                attempt.UserId,
                Username = attempt.Email, // Map Email property to Username column
                IsSuccessful = attempt.IsSuccess,
                attempt.FailureReason,
                attempt.IpAddress,
                attempt.UserAgent,
                attempt.AttemptedAt,
                attempt.ApplicationId
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAttempt>> GetRecentByUserAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<LoginAttemptDto>(@"
            SELECT TOP (@Count)
                [Id], [UserId], [Username] AS [Email], [IsSuccessful] AS [IsSuccess],
                [FailureReason], [IpAddress], [UserAgent], [AttemptedAt], [ApplicationId]
            FROM [dbo].[LoginAttempts]
            WHERE [UserId] = @UserId
            ORDER BY [AttemptedAt] DESC",
            new { UserId = userId, Count = count });

        return dtos.Select(dto => dto.ToEntity()).ToList();
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
