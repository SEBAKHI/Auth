using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;
using AppEntity = Auth_Lib.Domain.Entities.Application;

namespace Auth_Lib.Infrastructure.Data;

/// <summary>
/// Dapper implementation of the application repository.
/// </summary>
public class ApplicationRepository : IApplicationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ApplicationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<AppEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<AppEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Code] = @Code",
            new { Code = code.ToUpperInvariant() });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            ORDER BY [Code]");

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppEntity>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [IsActive] = 1
            ORDER BY [Code]");

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Applications]
            WHERE [Code] = @Code",
            new { Code = code.ToUpperInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<AppEntity> CreateAsync(AppEntity application, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Applications] (
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Code, @Name, @Description, @BaseUrl, @LogoUrl, @ContactEmail,
                @IsActive, @AllowSelfRegistration, @RequireTwoFactor, @RequireEmailVerification,
                @SessionTimeoutMinutes, @MaxConcurrentSessions,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                application.Id,
                application.Code,
                application.Name,
                application.Description,
                application.BaseUrl,
                application.LogoUrl,
                application.ContactEmail,
                application.IsActive,
                application.AllowSelfRegistration,
                application.RequireTwoFactor,
                application.RequireEmailVerification,
                application.SessionTimeoutMinutes,
                application.MaxConcurrentSessions,
                application.CreatedAt,
                application.CreatedBy,
                application.ModifiedAt,
                application.ModifiedBy
            });

        return application;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AppEntity application, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Applications] SET
                [Name] = @Name,
                [Description] = @Description,
                [BaseUrl] = @BaseUrl,
                [LogoUrl] = @LogoUrl,
                [ContactEmail] = @ContactEmail,
                [IsActive] = @IsActive,
                [AllowSelfRegistration] = @AllowSelfRegistration,
                [RequireTwoFactor] = @RequireTwoFactor,
                [RequireEmailVerification] = @RequireEmailVerification,
                [SessionTimeoutMinutes] = @SessionTimeoutMinutes,
                [MaxConcurrentSessions] = @MaxConcurrentSessions,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                application.Id,
                application.Name,
                application.Description,
                application.BaseUrl,
                application.LogoUrl,
                application.ContactEmail,
                application.IsActive,
                application.AllowSelfRegistration,
                application.RequireTwoFactor,
                application.RequireEmailVerification,
                application.SessionTimeoutMinutes,
                application.MaxConcurrentSessions,
                application.ModifiedAt,
                application.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Hard delete for applications (could be changed to soft delete if needed)
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[Applications]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    // Internal DTO for mapping from database
    private record ApplicationDto
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? BaseUrl { get; init; }
        public string? LogoUrl { get; init; }
        public string? ContactEmail { get; init; }
        public bool IsActive { get; init; }
        public bool AllowSelfRegistration { get; init; }
        public bool RequireTwoFactor { get; init; }
        public bool RequireEmailVerification { get; init; }
        public int SessionTimeoutMinutes { get; init; }
        public int MaxConcurrentSessions { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public AppEntity ToEntity() => new(
            Id,
            Code,
            Name,
            Description,
            BaseUrl,
            LogoUrl,
            ContactEmail,
            IsActive,
            AllowSelfRegistration,
            RequireTwoFactor,
            RequireEmailVerification,
            SessionTimeoutMinutes,
            MaxConcurrentSessions,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
