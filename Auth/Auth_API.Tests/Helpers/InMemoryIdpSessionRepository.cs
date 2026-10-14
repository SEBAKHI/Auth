using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The IdpSessions table as the Dapper repository changes it, for tests that walk a
/// timeline through several handlers (OI-154). Each method applies the conditions
/// its SQL applies: a lookup sees only rows not revoked; the activity write runs
/// only while the row is alive. The SQL itself is pinned by
/// <c>IdpSessionRepositorySqlTests</c> and was run against LocalDB (see the PR).
/// </summary>
internal sealed class InMemoryIdpSessionRepository : IIdpSessionRepository
{
    private readonly Dictionary<Guid, Row> _rows = [];

    /// <summary>How many times a revocation was written, per session.</summary>
    public Dictionary<Guid, int> Revocations { get; } = [];

    /// <summary>The stored state of one row.</summary>
    public sealed record Row(
        Guid Id,
        Guid UserId,
        string TokenHash,
        DateTime CreatedAt,
        DateTime ExpiresAt,
        DateTime? RevokedAt,
        bool? IsPersistent,
        DateTime? LastActivityAt);

    public Row this[Guid id] => _rows[id];

    /// <summary>Stores a row directly, the way an earlier build may have left it (NULL columns included).</summary>
    public void Seed(Row row) => _rows[row.Id] = row;

    public Task<IdpSession> CreateAsync(IdpSession session, CancellationToken cancellationToken)
    {
        _rows[session.Id] = new Row(
            session.Id, session.UserId, session.TokenHash, session.CreatedAt, session.ExpiresAt,
            session.RevokedAt, session.IsPersistent, session.LastActivityAt);
        return Task.FromResult(session);
    }

    // WHERE [TokenHash] = @TokenHash AND [RevokedAt] IS NULL
    public Task<IdpSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var row = _rows.Values.FirstOrDefault(r => r.TokenHash == tokenHash && r.RevokedAt is null);
        return Task.FromResult(row is null
            ? null
            : new IdpSession(
                row.Id, row.UserId, row.TokenHash, row.CreatedAt, row.ExpiresAt, row.RevokedAt, null, null,
                row.IsPersistent ?? false, row.LastActivityAt));
    }

    // UPDATE … SET [RevokedAt] = @RevokedAt WHERE [Id] = @Id
    public Task UpdateAsync(IdpSession session, CancellationToken cancellationToken)
    {
        _rows[session.Id] = _rows[session.Id] with { RevokedAt = session.RevokedAt };
        Revocations[session.Id] = Revocations.GetValueOrDefault(session.Id) + 1;
        return Task.CompletedTask;
    }

    // UPDATE … SET [LastActivityAt] = @Now WHERE [Id] = @Id AND [RevokedAt] IS NULL
    //   AND [ExpiresAt] > @Now AND ([IsPersistent] = 1 OR @IdleMinutes = 0
    //   OR COALESCE([LastActivityAt], [CreatedAt]) > @IdleCutoff)
    public Task<bool> RecordActivityAsync(
        Guid sessionId, DateTime nowUtc, TimeSpan idleLimit, CancellationToken cancellationToken)
    {
        if (!_rows.TryGetValue(sessionId, out var row))
        {
            return Task.FromResult(false);
        }

        var idleMinutes = (int)idleLimit.TotalMinutes;
        var alive = row.RevokedAt is null
            && row.ExpiresAt > nowUtc
            && (row.IsPersistent == true
                || idleMinutes == 0
                || (row.LastActivityAt ?? row.CreatedAt) > nowUtc.AddMinutes(-idleMinutes));

        if (alive)
        {
            _rows[sessionId] = row with { LastActivityAt = nowUtc };
        }

        return Task.FromResult(alive);
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<int> RevokeAllForUserExceptAsync(Guid userId, string? exceptTokenHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");
}
