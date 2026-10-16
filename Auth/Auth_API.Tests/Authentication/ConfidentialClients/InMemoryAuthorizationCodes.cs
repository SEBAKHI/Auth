using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// The AuthorizationCodes table as the Dapper repository changes it (OI-171). Each consume
/// applies the conditions of its statement (pinned by <c>ConfidentialClientPersistenceTests</c>):
/// the public consume takes only a public code, the authenticated one only a code of the
/// application that authenticated. Reads hand out copies, as Dapper does.
/// </summary>
internal sealed class InMemoryAuthorizationCodes : IAuthorizationCodeRepository
{
    private readonly Dictionary<string, AuthorizationCode> _rows = [];

    public void Seed(AuthorizationCode code) => _rows[code.CodeHash] = Copy(code);

    public AuthorizationCode Stored(string codeHash) => Copy(_rows[codeHash]);

    public Task<AuthorizationCode> CreateAsync(AuthorizationCode code, CancellationToken cancellationToken)
    {
        Seed(code);
        return Task.FromResult(code);
    }

    // … WHERE [CodeHash] = @CodeHash AND [ConsumedAt] IS NULL AND [RequiresClientAuthentication] IS NULL
    public Task<AuthorizationCode?> ConsumeByCodeHashAsync(string codeHash, CancellationToken cancellationToken) =>
        Task.FromResult(Consume(codeHash, row => !row.RequiresClientAuthentication));

    // SET … [RequiresClientAuthentication] = 1 … WHERE [CodeHash] = @CodeHash AND [ConsumedAt] IS NULL AND [ApplicationId] = @ApplicationId
    public Task<AuthorizationCode?> ConsumeForClientAsync(string codeHash, Guid applicationId, CancellationToken cancellationToken) =>
        Task.FromResult(Consume(codeHash, row => row.ApplicationId == applicationId, markServerSide: true));

    public Task<AuthorizationCode?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.TryGetValue(codeHash, out var row) ? Copy(row) : null);

    public Task RecordIssuedSessionAsync(Guid codeId, Guid sessionId, CancellationToken cancellationToken)
    {
        var row = _rows.Values.Single(r => r.Id == codeId);
        _rows[row.CodeHash] = Copy(row, consumedAt: row.ConsumedAt, issuedSessionId: sessionId);
        return Task.CompletedTask;
    }

    public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not part of the OI-171 flows.");

    private AuthorizationCode? Consume(string codeHash, Func<AuthorizationCode, bool> mayConsume, bool markServerSide = false)
    {
        if (!_rows.TryGetValue(codeHash, out var row) || row.IsConsumed || !mayConsume(row))
        {
            return null;
        }

        var consumed = Copy(row, consumedAt: DateTime.UtcNow, issuedSessionId: row.IssuedSessionId,
            requiresClientAuthentication: row.RequiresClientAuthentication || markServerSide);
        _rows[codeHash] = consumed;
        return Copy(consumed);
    }

    private static AuthorizationCode Copy(AuthorizationCode row) =>
        Copy(row, row.ConsumedAt, row.IssuedSessionId);

    private static AuthorizationCode Copy(
        AuthorizationCode row, DateTime? consumedAt, Guid? issuedSessionId, bool? requiresClientAuthentication = null) => new(
        row.Id, row.ApplicationId, row.UserId, row.CodeHash, row.RedirectUri, row.CodeChallenge,
        row.CreatedAt, row.ExpiresAt, consumedAt, row.IpAddress, issuedSessionId, row.Scope, row.IdpSessionId,
        requiresClientAuthentication ?? row.RequiresClientAuthentication);
}
