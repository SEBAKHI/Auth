using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The RefreshTokens table as the Dapper repository changes it, for the OI-154
/// timelines that follow a refresh chain across a re-sign-in. Reads hand out
/// copies, as Dapper does, so a handler that revokes the entity it read changes
/// nothing until it writes. Each write applies the conditions its SQL applies
/// (pinned by <c>RefreshTokenRepositorySqlGuardTests</c>).
/// </summary>
internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Dictionary<Guid, RefreshToken> _rows = [];

    /// <summary>The stored state of every row.</summary>
    public IReadOnlyCollection<RefreshToken> Rows => _rows.Values;

    /// <summary>Stores a row directly.</summary>
    public void Seed(RefreshToken token) => _rows[token.Id] = Copy(token);

    /// <summary>The stored row with this hash.</summary>
    public RefreshToken ByHash(string tokenHash) => _rows.Values.Single(r => r.TokenHash == tokenHash);

    public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.TryGetValue(id, out var row) ? Copy(row) : null);

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.Values.FirstOrDefault(r => r.TokenHash == tokenHash) is { } row ? Copy(row) : null);

    public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        _rows[token.Id] = Copy(token);
        return Task.FromResult(token);
    }

    public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        _rows[token.Id] = Copy(token);
        return Task.CompletedTask;
    }

    // UPDATE … WHERE [Id] = @Id AND [RevokedAt] IS NULL; then the INSERT, in one transaction.
    public Task<bool> TryRotateAsync(RefreshToken revokedOld, RefreshToken replacement, CancellationToken cancellationToken)
    {
        if (!_rows.TryGetValue(revokedOld.Id, out var stored) || stored.IsRevoked)
        {
            return Task.FromResult(false);
        }

        _rows[revokedOld.Id] = Copy(revokedOld);
        _rows[replacement.Id] = Copy(replacement);
        return Task.FromResult(true);
    }

    // UPDATE … WHERE [SessionId] = @SessionId AND [RevokedAt] IS NULL
    public Task RevokeBySessionIdAsync(Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken)
    {
        foreach (var row in _rows.Values.Where(r => r.SessionId == sessionId && !r.IsRevoked).ToList())
        {
            var revoked = Copy(row);
            revoked.Revoke(revokedBy, reason);
            _rows[row.Id] = revoked;
        }

        return Task.CompletedTask;
    }

    // SELECT … WHERE [SessionId] = @SessionId AND [RevokedAt] IS NULL AND [ExpiresAt] > GETUTCDATE()
    public Task<bool> HasLiveTokenInSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.Values.Any(r => r.SessionId == sessionId && !r.IsRevoked && !r.IsExpired()));

    // UPDATE … SET [IdpSessionId] = @To WHERE [IdpSessionId] = @From AND [UserId] = @UserId AND [RevokedAt] IS NULL
    public Task<int> RelinkIdpSessionAsync(
        Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken)
    {
        var moved = _rows.Values
            .Where(r => r.IdpSessionId == fromIdpSessionId && r.UserId == userId && !r.IsRevoked)
            .ToList();

        foreach (var row in moved)
        {
            _rows[row.Id] = Copy(row, toIdpSessionId);
        }

        return Task.FromResult(moved.Count);
    }

    public Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<IReadOnlyList<Guid>> RevokeAllForApplicationAsync(Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<IReadOnlyList<Guid>> RevokeForUserAndApplicationAsync(Guid userId, Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-154 timelines.");

    private static RefreshToken Copy(RefreshToken row, Guid? idpSessionId = null) =>
        TestHelpers.CreateRefreshToken(
            id: row.Id,
            userId: row.UserId,
            tokenHash: row.TokenHash,
            jwtId: row.JwtId,
            applicationId: row.ApplicationId,
            deviceInfo: row.DeviceInfo,
            ipAddress: row.IpAddress,
            createdAt: row.CreatedAt,
            expiresAt: row.ExpiresAt,
            revokedAt: row.RevokedAt,
            revokedBy: row.RevokedBy,
            replacedByTokenHash: row.ReplacedByTokenHash,
            reasonRevoked: row.ReasonRevoked,
            sessionId: row.SessionId,
            scope: row.Scope,
            idpSessionId: idpSessionId ?? row.IdpSessionId);
}
