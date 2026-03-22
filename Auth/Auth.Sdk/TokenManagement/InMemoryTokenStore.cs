namespace Auth.Sdk.TokenManagement;

/// <summary>
/// Thread-safe in-memory token store. Suitable for single-instance applications.
/// For multi-instance deployments, implement <see cref="ITokenStore"/> with distributed cache.
/// </summary>
public class InMemoryTokenStore : ITokenStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TokenSet? _tokens;

    /// <inheritdoc />
    public async Task<TokenSet?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _tokens;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string accessToken, string refreshToken, int expiresInSeconds, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _tokens = new TokenSet(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _tokens = null;
        }
        finally
        {
            _lock.Release();
        }
    }
}
