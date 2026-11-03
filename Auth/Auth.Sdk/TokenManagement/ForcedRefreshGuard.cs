namespace Auth.Sdk.TokenManagement;

/// <summary>
/// Remembers the access token a forced refresh produced until a request with it succeeds (SDK 2.1).
/// A token the AuthSystem refuses is refreshed once; if the replacement is refused too, refreshing
/// again cannot help (the refusal is not about the token's age) and would spend a refresh on every
/// request. Registered once per container, beside the token store it guards.
/// </summary>
internal sealed class ForcedRefreshGuard
{
    private readonly Lock _gate = new();
    private string? _unproven;

    /// <summary>True when <paramref name="accessToken"/> came from a forced refresh and no request with it has succeeded yet.</summary>
    public bool IsUnproven(string accessToken)
    {
        lock (_gate)
        {
            return string.Equals(_unproven, accessToken, StringComparison.Ordinal);
        }
    }

    /// <summary>A forced refresh produced <paramref name="accessToken"/>.</summary>
    public void Produced(string accessToken)
    {
        lock (_gate)
        {
            _unproven = accessToken;
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
