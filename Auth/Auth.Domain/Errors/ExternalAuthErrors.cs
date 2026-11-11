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

    /// <summary>
    /// The provider refused to turn the one-time code it sent back into a token: the code
    /// was already spent, expired, or does not belong to this sign-in (its PKCE verifier or
    /// its return address differs). Starting the sign-in again fetches a new code.
    /// </summary>
    public static Error CodeExchangeFailed => Error.Validation(
        code: "ExternalAuth.CodeExchangeFailed",
        description: "The sign-in with the provider could not be completed. Please try again.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error CodeRequired = Error.Validation(
        code: "ExternalAuth.CodeRequired",
        description: "The provider's code is required.");

    public static readonly Error CodeTooLong = Error.Validation(
        code: "ExternalAuth.CodeTooLong",
        description: "The provider's code must not exceed 512 characters.");

    public static readonly Error CodeVerifierInvalid = Error.Validation(
        code: "ExternalAuth.CodeVerifierInvalid",
        description: "The code verifier must be 43 to 128 letters, digits, or the characters - . _ ~.");

    public static readonly Error AuthorizationCodeTooLong = Error.Validation(
        code: "ExternalAuth.AuthorizationCodeTooLong",
        description: "Authorization code must not exceed 2000 characters.");

    public static readonly Error FamilyNameTooLong = Error.Validation(
        code: "ExternalAuth.FamilyNameTooLong",
        description: "Last name must not exceed 100 characters.");

    public static readonly Error GivenNameTooLong = Error.Validation(
        code: "ExternalAuth.GivenNameTooLong",
        description: "First name must not exceed 100 characters.");

    public static readonly Error IdTokenRequired = Error.Validation(
        code: "ExternalAuth.IdTokenRequired",
        description: "ID token is required.");

    public static readonly Error ProviderRequired = Error.Validation(
        code: "ExternalAuth.ProviderRequired",
        description: "Provider is required.");

    public static readonly Error ProviderTooLong = Error.Validation(
        code: "ExternalAuth.ProviderTooLong",
        description: "Provider name is too long.");
}
