using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the known-device repository.
/// </summary>
public class UserKnownDeviceRepository : IUserKnownDeviceRepository
{
    private const string SelectColumns =
        "[Id], [UserId], [DeviceHash], [DeviceName], [FirstSeenAt], [LastSeenAt], [LastAlertSentAt]";

    private readonly IDbConnectionFactory _connectionFactory;

    public UserKnownDeviceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserKnownDevice?> GetAsync(
        Guid userId,
        string deviceHash,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<UserKnownDeviceDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserKnownDevices]
            WHERE [UserId] = @UserId AND [DeviceHash] = @DeviceHash",
            new { UserId = userId, DeviceHash = deviceHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(@"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserKnownDevices] WHERE [UserId] = @UserId
            ) THEN 1 ELSE 0 END AS BIT)",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLastAlertAtAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<DateTime?>(@"
            SELECT MAX([LastAlertSentAt])
            FROM [dbo].[UserKnownDevices]
            WHERE [UserId] = @UserId",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserKnownDevice>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<UserKnownDeviceDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserKnownDevices]
            WHERE [UserId] = @UserId
            ORDER BY [LastSeenAt] DESC, [Id]",
            new { UserId = userId });

        return rows.Select(r => r.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<UserKnownDevice?> GetByIdAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<UserKnownDeviceDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserKnownDevices]
            WHERE [Id] = @Id AND [UserId] = @UserId",
            new { Id = deviceId, UserId = userId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The UserId predicate is the authorization check, not a convenience:
        // it must be impossible to delete another user's row by guessing an id.
        var affected = await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[UserKnownDevices]
            WHERE [Id] = @Id AND [UserId] = @UserId",
            new { Id = deviceId, UserId = userId });

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> AdoptLegacySignatureAsync(
        Guid userId,
        string legacyHash,
        string currentHash,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        // CreateConnectionAsync hands back an open connection; opening it again
        // throws, and the failure surfaces far from here.
        using var transaction = connection.BeginTransaction();

        var parameters = new { UserId = userId, LegacyHash = legacyHash, CurrentHash = currentHash };

        var legacyExists = await connection.ExecuteScalarAsync<bool>(@"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserKnownDevices]
                WHERE [UserId] = @UserId AND [DeviceHash] = @LegacyHash
            ) THEN 1 ELSE 0 END AS BIT)",
            parameters, transaction);

        if (!legacyExists)
        {
            transaction.Rollback();
            return false;
        }

        // Renaming onto a signature the user already has would violate
        // UQ_UserKnownDevices_UserDevice. Both branches converge on one row for
        // the browser, which is the point.
        var currentExists = await connection.ExecuteScalarAsync<bool>(@"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserKnownDevices]
                WHERE [UserId] = @UserId AND [DeviceHash] = @CurrentHash
            ) THEN 1 ELSE 0 END AS BIT)",
            parameters, transaction);

        if (currentExists)
        {
            await connection.ExecuteAsync(@"
                DELETE FROM [dbo].[UserKnownDevices]
                WHERE [UserId] = @UserId AND [DeviceHash] = @LegacyHash",
                parameters, transaction);
        }
        else
        {
            // Renamed rather than replaced, so FirstSeenAt keeps saying when this
            // browser was actually first seen.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[UserKnownDevices]
                SET [DeviceHash] = @CurrentHash
                WHERE [UserId] = @UserId AND [DeviceHash] = @LegacyHash",
                parameters, transaction);
        }

        // Sessions filed under the old signature follow the row, or they would
        // drop into the unattributed bucket the moment the row moved.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions]
            SET [DeviceHash] = @CurrentHash
            WHERE [UserId] = @UserId AND [DeviceHash] = @LegacyHash",
            parameters, transaction);

        transaction.Commit();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(UserKnownDevice device, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // UPDATE-then-INSERT rather than MERGE: two sign-ins from the same
        // device can race, and the loser must land as an update instead of
        // violating UQ_UserKnownDevices_UserDevice and failing the login.
        var updated = await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserKnownDevices]
            SET [LastSeenAt] = @LastSeenAt,
                [DeviceName] = COALESCE(@DeviceName, [DeviceName]),
                [LastAlertSentAt] = COALESCE(@LastAlertSentAt, [LastAlertSentAt])
            WHERE [UserId] = @UserId AND [DeviceHash] = @DeviceHash",
            new
            {
                device.UserId,
                device.DeviceHash,
                device.DeviceName,
                device.LastSeenAt,
                device.LastAlertSentAt
            });

        if (updated > 0)
        {
            return false;
        }

        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[UserKnownDevices]
                    ([Id], [UserId], [DeviceHash], [DeviceName], [FirstSeenAt], [LastSeenAt], [LastAlertSentAt])
                VALUES
                    (@Id, @UserId, @DeviceHash, @DeviceName, @FirstSeenAt, @LastSeenAt, @LastAlertSentAt)",
                new
                {
                    device.Id,
                    device.UserId,
                    device.DeviceHash,
                    device.DeviceName,
                    device.FirstSeenAt,
                    device.LastSeenAt,
                    device.LastAlertSentAt
                });

            return true;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent sign-in inserted first; fold this sighting into it
            // and report that this call did not discover the device.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[UserKnownDevices]
                SET [LastSeenAt] = @LastSeenAt
                WHERE [UserId] = @UserId AND [DeviceHash] = @DeviceHash",
                new { device.UserId, device.DeviceHash, device.LastSeenAt });

            return false;
        }
    }

    private record UserKnownDeviceDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string DeviceHash { get; init; } = string.Empty;
        public string? DeviceName { get; init; }
        public DateTime FirstSeenAt { get; init; }
        public DateTime LastSeenAt { get; init; }
        public DateTime? LastAlertSentAt { get; init; }

        public UserKnownDevice ToEntity() => new(
            Id, UserId, DeviceHash, DeviceName, FirstSeenAt, LastSeenAt, LastAlertSentAt);
    }
}
