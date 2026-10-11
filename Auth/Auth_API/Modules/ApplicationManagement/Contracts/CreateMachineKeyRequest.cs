namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Body of <c>POST api/v1/applications/{applicationId}/machine-keys</c> (OI-128).
/// </summary>
public class CreateMachineKeyRequest
{
    /// <summary>An optional label for the key.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// A public P-256 key as a JWK (<c>kty</c> <c>EC</c>, <c>crv</c> <c>P-256</c>, <c>x</c>,
    /// <c>y</c>), as a JSON string. Omit it to have the server generate the pair and return the
    /// private key once.
    /// </summary>
    public string? PublicKeyJwk { get; set; }
}
