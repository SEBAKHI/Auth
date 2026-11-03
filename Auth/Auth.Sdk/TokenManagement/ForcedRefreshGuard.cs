namespace Auth.Sdk.TokenManagement;

/// <summary>
/// One forced refresh per refused access token (SDK 2.1). A token the AuthSystem refuses is
/// refreshed once; two outcomes then stop a second forced refresh for it:
/// <list type="bullet">
/// <item>its replacement is refused too: the refusal is not about the token's age, so the
/// replacement is not force-refreshed until a request with it succeeds;</item>
/// <item>its refresh produced nothing (the AuthSystem failed, was unreachable, or asked to wait):
/// the refused token is not force-refreshed again, and the refresh at its expiry takes over.</item>
/// </list>
/// Without either, every request still carrying the token would spend a refresh, exactly when
/// the AuthSystem is busiest. Registered once per container, beside the token store it guards.
/// </summary>
internal sealed class ForcedRefreshGuard
{
    private readonly Lock _gate = new();
    private string? _unproven;
    private string? _refreshFailed;

    /// <summary>True when <paramref name="accessToken"/> came from a forced refresh and no request with it has succeeded yet.</summary>
    public bool IsUnproven(string accessToken)
    {
        lock (_gate)
        {
            return string.Equals(_unproven, accessToken, StringComparison.Ordinal);
        }
    }

    /// <summary>True when the forced refresh of the refused <paramref name="accessToken"/> already produced nothing.</summary>
    public bool RefreshFailedFor(string accessToken)
    {
        lock (_gate)
        {
            return string.Equals(_refreshFailed, accessToken, StringComparison.Ordinal);
        }
    }

    /// <summary>A forced refresh produced <paramref name="accessToken"/>.</summary>
    public void Produced(string accessToken)
    {
        lock (_gate)
        {
            _unproven = accessToken;
            _refreshFailed = null;
        }
    }

    /// <summary>The forced refresh of the refused <paramref name="accessToken"/> produced nothing.</summary>
    public void RefreshFailed(string accessToken)
    {
        lock (_gate)
        {
            _refreshFailed = accessToken;
        }
    }

    /// <summary>A request carrying <paramref name="accessToken"/> succeeded: it may be force-refreshed again later.</summary>
    public void Succeeded(string accessToken)
    {
        lock (_gate)
        {
            if (string.Equals(_unproven, accessToken, StringComparison.Ordinal))
            {
                _unproven = null;
            }
        }
    }
}
