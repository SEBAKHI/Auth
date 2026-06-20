using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to secret management operations.
/// </summary>
public static class SecretErrors
{
    public static Error DecryptionFailed => Error.Failure(
        code: "Secret.DecryptionFailed",
        description: "Failed to decrypt the secret file. It may have been encrypted on a different machine or the DPAPI keys may have changed.");

    public static Error FileAccessFailed => Error.Failure(
        code: "Secret.FileAccessFailed",
        description: "Failed to access the secret file. Check file permissions and path configuration.");

    public static Error KeyGenerationFailed => Error.Failure(
        code: "Secret.KeyGenerationFailed",
        description: "Failed to generate cryptographic key.");

    public static Error InvalidSecretKey => Error.Validation(
        code: "Secret.InvalidKey",
        description: "Secret key must be alphanumeric with underscores or dots only, and less than 100 characters.");

    public static Error UnknownSecretKey(string key) => Error.Validation(
        code: "Secret.UnknownKey",
        description: $"Unknown secret key: {key}.",
        metadata: new() { ["args"] = new object[] { key } });

    public static Error SecretNotFound(string key) => Error.NotFound(
        code: "Secret.NotFound",
        description: $"Custom secret '{key}' was not found.",
        metadata: new() { ["args"] = new object[] { key } });

    public static Error InvalidKeyMaterial(string detail) => Error.Validation(
        code: "Secret.InvalidKeyMaterial",
        description: $"The supplied key material is invalid: {detail}",
        metadata: new() { ["args"] = new object[] { detail } });

    public static Error ImportNotSupportedInPlainText => Error.Conflict(
        code: "Secret.ImportNotSupportedInPlainText",
        description: "Importing keys via the admin API is only supported in Certificate or Dpapi storage mode. " +
                     "In PlainText mode, set the keys directly in appsettings.Production.json.");
}
