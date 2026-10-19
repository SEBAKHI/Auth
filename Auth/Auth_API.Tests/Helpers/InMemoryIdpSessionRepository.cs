using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The IdpSessions table as the Dapper repository changes it, for tests that walk a
/// timeline through several handlers (OI-154). Each method applies the conditions
/// its SQL applies: the cookie lookup sees only rows not revoked, its sign-out
/// counterpart every row (OI-155); the activity write runs only while the row is alive. The SQL itself is pinned by
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

    /// <summary>How many times the live cookie lookup was called.</summary>
    public int LiveLookups { get; private set; }

    // WHERE [TokenHash] = @TokenHash AND [RevokedAt] IS NULL
    public Task<IdpSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        LiveLookups++;
        var row = _rows.Values.FirstOrDefault(r => r.TokenHash == tokenHash && r.RevokedAt is null);
        return Task.FromResult(row is null
            ? null
            : new IdpSession(
                row.Id, row.UserId, row.TokenHash, row.CreatedAt, row.ExpiresAt, row.RevokedAt, null, null,
                row.IsPersistent ?? false, row.LastActivityAt));
    }

    /// <summary>How many times the sign-out lookup, revoked rows included, was called.</summary>
    public int LookupsIncludingRevoked { get; private set; }

    // WHERE [TokenHash] = @TokenHash
    public Task<IdpSession?> GetByTokenHashIncludingRevokedAsync(string tokenHash, CancellationToken cancellationToken)
    {
        LookupsIncludingRevoked++;
        var row = _rows.Values.FirstOrDefault(r => r.TokenHash == tokenHash);
        return Task.FromResult(row is null
            ? null
            : new IdpSession(
                row.Id, row.UserId, row.TokenHash, row.CreatedAt, row.ExpiresAt, row.RevokedAt, null, null,
                row.IsPersistent ?? false, row.LastActivityAt));
    }

    // WHERE [Id] = @Id
    public Task<IdpSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.TryGetValue(id, out var row)
            ? new IdpSession(
                row.Id, row.UserId, row.TokenHash, row.CreatedAt, row.ExpiresAt, row.RevokedAt, null, null,
                row.IsPersistent ?? false, row.LastActivityAt)
            : null);

    /// <summary>Removes a row, the way the retention sweep deletes it.</summary>
    public void Purge(Guid id) => _rows.Remove(id);

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

    // UPDATE … SET [RevokedAt] = @Now WHERE [RevokedAt] IS NULL AND ISNULL([IsPersistent], 0) = 0
    //   AND [ExpiresAt] > @Now AND COALESCE([LastActivityAt], [CreatedAt]) <= @IdleCutoff
    public Task<int> EndIdleBrowserSessionsAsync(
        DateTime nowUtc, DateTime lastUsedAtOrBefore, CancellationToken cancellationToken)
    {
        var idle = _rows.Values
            .Where(r => r.RevokedAt is null
                && r.IsPersistent != true
                && r.ExpiresAt > nowUtc
                && (r.LastActivityAt ?? r.CreatedAt) <= lastUsedAtOrBefore)
            .ToList();

        foreach (var row in idle)
        {
            _rows[row.Id] = row with { RevokedAt = nowUtc };
        }

        BulkIdleEndings++;
        return Task.FromResult(idle.Count);
    }

    /// <summary>How many times the set-based idle ending ran.</summary>
    public int BulkIdleEndings { get; private set; }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<int> RevokeAllForUserExceptAsync(Guid userId, string? exceptTokenHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");
}
