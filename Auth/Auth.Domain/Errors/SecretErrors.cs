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

    /// <summary>
    /// PlainText mode has no encrypted file to write into, so storing the
    /// connection string or the SMTP password there would be a no-op that reads
    /// as success. In that mode both belong in configuration or an environment
    /// variable, which is where the process already reads them from.
    /// </summary>
    public static Error SetNotSupportedInPlainText => Error.Conflict(
        code: "Secret.SetNotSupportedInPlainText",
        description: "Storing this secret is only supported in Certificate or Dpapi storage mode. " +
                     "In PlainText mode, supply it through configuration or an environment variable instead.");

    /// <summary>
    /// The supplied text is not a well-formed SQL Server connection string. This
    /// is a hard rejection: unlike an unreachable server, a malformed string can
    /// never become valid later, so there is nothing to pre-stage.
    /// </summary>
    public static Error ConnectionStringMalformed(string detail) => Error.Validation(
        code: "Secret.ConnectionStringMalformed",
        description: $"The connection string could not be parsed: {detail}",
        metadata: new() { ["args"] = new object[] { detail } });

    /// <summary>
    /// The connection string parsed but no connection could be opened with it.
    /// Recoverable by design — an operator pre-staging a password change stores a
    /// value that is not live yet — so the caller may repeat the request with
    /// <c>ForceSave</c> to store it anyway.
    /// </summary>
    public static Error ConnectionStringUnreachable(string detail) => Error.Validation(
        code: "Secret.ConnectionStringUnreachable",
        description: $"The connection string was not saved because no connection could be opened with it: {detail} " +
                     "If you are staging a password that is not active yet, resubmit with confirmation to save it anyway.",
        metadata: new() { ["args"] = new object[] { detail } });

    /// <summary>
    /// The single failure shape for entering a confirmation code: wrong code,
    /// expired code, spent code, exhausted attempts and unknown challenge id all
    /// return this. Distinguishing them would tell a guesser which of their
    /// assumptions was right.
    /// </summary>
    public static Error InvalidChallengeCode => Error.Validation(
        code: "Secret.InvalidChallengeCode",
        description: "The confirmation code is incorrect or is no longer valid. Request a new code and try again.");

    /// <summary>
    /// The single failure shape for spending an approval: unverified, expired,
    /// already spent, requested by a different administrator, or bound to a
    /// different operation or different key material.
    /// </summary>
    public static Error ChallengeNotApproved => Error.Forbidden(
        code: "Secret.ChallengeNotApproved",
        description: "This operation has not been confirmed, or the confirmation has expired. " +
                     "Start again and confirm with a new code.");

    public static Error TooManyChallengeRequests => Error.Forbidden(
        code: "Secret.TooManyChallengeRequests",
        description: "Too many confirmation codes were requested. Please wait before trying again.");

    /// <summary>
    /// The requesting administrator has no confirmed address to send the code
    /// to. Rotating a signing key on the say-so of an account nobody can reach
    /// defeats the point of the second factor, so the operation stops here.
    /// </summary>
    public static Error ChallengeRecipientUnavailable => Error.Conflict(
        code: "Secret.ChallengeRecipientUnavailable",
        description: "Your account has no confirmed email address, so a confirmation code cannot be sent. " +
                     "Confirm your email address before performing this operation.");

    public static Error ChallengeEmailFailed => Error.Failure(
        code: "Secret.ChallengeEmailFailed",
        description: "Failed to send the confirmation code email. Please try again.");
}
