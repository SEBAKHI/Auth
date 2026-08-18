using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to external authentication operations.
/// </summary>
public static class ExternalAuthErrors
{
    public static Error ProviderNotSupported(string provider) => Error.Validation(
        code: "ExternalAuth.ProviderNotSupported",
        description: $"The external authentication provider '{provider}' is not supported.",
        metadata: new() { ["args"] = new object[] { provider } });

    public static Error TokenVerificationFailed => Error.Validation(
        code: "ExternalAuth.TokenVerificationFailed",
        description: "The external authentication token could not be verified.");

    public static Error ProviderNotConfigured(string provider) => Error.Validation(
        code: "ExternalAuth.ProviderNotConfigured",
        description: $"The external authentication provider '{provider}' is not configured.",
        metadata: new() { ["args"] = new object[] { provider } });

    public static Error EmailNotVerifiedByProvider => Error.Forbidden(
        code: "ExternalAuth.EmailNotVerified",
        description: "The email address has not been verified by the external provider.");

    public static Error AccountLinkConflict(string provider) => Error.Conflict(
        code: "ExternalAuth.AccountLinkConflict",
        description: $"This {provider} account is already linked to another user.",
        metadata: new() { ["args"] = new object[] { provider } });

    /// <summary>
    /// The sign-in presented no nonce, or one this server did not issue to this
    /// browser. Deliberately says nothing about which of the two it was.
    /// </summary>
    public static Error NonceRequired => Error.Validation(
        code: "ExternalAuth.NonceRequired",
        description: "The sign-in could not be verified. Please try again.");
}
