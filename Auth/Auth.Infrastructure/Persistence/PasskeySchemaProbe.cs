using Auth.Application.Interfaces;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// <see cref="IPasskeySchema"/> by asking the database once: <c>OBJECT_ID</c> for the two
/// tables and <c>COL_LENGTH</c> for the column.
/// </summary>
/// <remarks>
/// A singleton. "Present" is kept for the life of the process: the schema is additive and is
/// never removed. "Absent" is asked again after <see cref="RecheckAfter"/>, so a DACPAC
/// published while the API runs is picked up without a restart, at the cost of one trivial
/// query a minute until then. A failed probe propagates to the caller, like the read it
/// guards would have.
/// </remarks>
public sealed class PasskeySchemaProbe : IPasskeySchema
{
    /// <summary>How long an "absent" answer is kept before the database is asked again.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(1);

    internal const string ProbeSql = @"
            SELECT CASE WHEN OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NOT NULL
                          AND OBJECT_ID(N'[dbo].[WebAuthnChallenges]', N'U') IS NOT NULL
                          AND COL_LENGTH(N'[dbo].[Users]', N'WebAuthnUserHandle') IS NOT NULL
                        THEN 1 ELSE 0 END";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;
    private volatile bool _present;
    private long _absentUntilTicks;

    public PasskeySchemaProbe(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<bool> IsPresentAsync(CancellationToken cancellationToken)
    {
        if (_present)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow().UtcTicks;
        if (now < Interlocked.Read(ref _absentUntilTicks))
        {
            return false;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var present = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            ProbeSql, cancellationToken: cancellationToken));

        if (present)
        {
            _present = true;
        }
        else
        {
            Interlocked.Exchange(ref _absentUntilTicks, now + RecheckAfter.Ticks);
        }

        return present;
    }
}
