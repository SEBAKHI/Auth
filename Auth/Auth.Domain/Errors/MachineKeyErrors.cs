using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors of an application's machine keys (OI-128).
/// </summary>
public static class MachineKeyErrors
{
    /// <summary>No such key, or a key of another application: the two are one answer.</summary>
    public static readonly Error NotFound = Error.NotFound(
        code: "MachineKey.NotFound",
        description: "The machine key was not found.");

    public static readonly Error AlreadyRevoked = Error.Conflict(
        code: "MachineKey.AlreadyRevoked",
        description: "The machine key has already been revoked.");

    public static readonly Error LimitReached = Error.Conflict(
        code: "MachineKey.LimitReached",
        description: "The application already holds the most active keys of this purpose allowed. Revoke one first.");

    /// <summary>
    /// The pasted key is not a public P-256 key in JWK form (another key type or curve, a missing
    /// coordinate, a point off the curve, a private member such as <c>d</c>, any member outside
    /// the list the description names), or it is already registered: one public key belongs to
    /// one application.
    /// </summary>
    public static readonly Error InvalidPublicKey = Error.Validation(
        code: "MachineKey.InvalidPublicKey",
        description: "The public key must be an EC P-256 JWK, not already registered: kty, crv, x and y; "
            + "optionally kid, use \"sig\", alg \"ES256\", ext and key_ops [\"verify\"]; no private or other member.");

    /// <summary>The key's purpose is neither machine nor sign-in (OI-171).</summary>
    public static readonly Error PurposeInvalid = Error.Validation(
        code: "MachineKey.PurposeInvalid",
        description: "The key's purpose must be Machine or SignIn.");
}
