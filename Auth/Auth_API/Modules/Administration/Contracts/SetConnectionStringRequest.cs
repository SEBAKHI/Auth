namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request for storing the AuthDb connection string in the encrypted secrets file.
/// </summary>
public record SetConnectionStringRequest
{
    /// <summary>
    /// The full connection string.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Store the value even when no connection can be opened with it.
    /// </summary>
    /// <remarks>
    /// Set this only after the caller has been shown the failure and confirmed.
    /// Its purpose is staging a database password that has not been switched over
    /// at the server yet — the one case where storing an unusable value is
    /// correct.
    /// </remarks>
    public bool ForceSave { get; init; }
}
