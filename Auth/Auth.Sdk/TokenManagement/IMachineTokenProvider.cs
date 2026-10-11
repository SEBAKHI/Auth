namespace Auth.Sdk.TokenManagement;

/// <summary>
/// Obtains this application's machine access token from the AuthSystem (the client_credentials
/// grant with private_key_jwt), and keeps it until shortly before it expires.
/// </summary>
/// <remarks>
/// Requires <see cref="AuthSystemOptions.ClientId"/>, <see cref="AuthSystemOptions.MachineKeyId"/>
/// and <see cref="AuthSystemOptions.MachinePrivateKeyPem"/>. The private key never leaves this
/// process: what goes over the wire is a one-minute statement signed with it.
/// </remarks>
public interface IMachineTokenProvider
{
    /// <summary>
    /// A machine access token valid for at least one more minute: the cached one, or a new one.
    /// Concurrent callers share one token request.
    /// </summary>
    /// <exception cref="InvalidOperationException">The three machine options are not all set, or the private key is not a P-256 PKCS#8 PEM.</exception>
    /// <exception cref="MachineTokenException">The AuthSystem refused the request or could not be reached.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
