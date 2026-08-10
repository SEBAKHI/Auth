namespace Auth.Domain.Enums;

/// <summary>
/// A destructive secret-management operation that may only run behind a
/// verified <see cref="Entities.SecretOperationChallenge"/>. Stored as TINYINT
/// in SecretOperationChallenges (the CK constraint must match).
/// </summary>
/// <remarks>
/// The challenge is bound to exactly one member of this enum, so an approval
/// obtained for the operation with the smallest blast radius (a gateway token,
/// which invalidates no user credential at all) can never be replayed against
/// the one with the largest (the refresh-token HMAC key, which signs everybody
/// out). Members serialize by name over the wire, so adding one requires a
/// matching CK value in the table and nothing else.
/// </remarks>
public enum SecretOperation : byte
{
    /// <summary>Regenerate the RSA key pair that signs access tokens.</summary>
    GenerateRsaKey = 1,

    /// <summary>Regenerate the HMAC key that hashes refresh tokens.</summary>
    GenerateHmacKey = 2,

    /// <summary>Regenerate the gateway token shared by the API and the gateway.</summary>
    GenerateGatewayToken = 3,

    /// <summary>Replace the RSA signing key with caller-supplied key material.</summary>
    ImportRsaKey = 4,

    /// <summary>Replace the refresh-token HMAC key with caller-supplied key material.</summary>
    ImportHmacKey = 5,

    /// <summary>Replace the gateway token with a caller-supplied value.</summary>
    ImportGatewayToken = 6
}
