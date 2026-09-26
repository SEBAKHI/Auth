using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to webhook key operations.
/// </summary>
public static class WebhookKeyErrors
{
    public static Error NotFound => Error.NotFound(
        code: "WebhookKey.NotFound",
        description: "The webhook key was not found.");

    public static Error Invalid => Error.Validation(
        code: "WebhookKey.Invalid",
        description: "The provided webhook key is invalid.");

    public static Error Revoked => Error.Forbidden(
        code: "WebhookKey.Revoked",
        description: "The webhook key has been revoked.");

    public static Error Expired => Error.Validation(
        code: "WebhookKey.Expired",
        description: "The webhook key has expired.");

    public static Error AlreadyRevoked => Error.Conflict(
        code: "WebhookKey.AlreadyRevoked",
        description: "The webhook key has already been revoked.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error IdRequired = Error.Validation(
        code: "WebhookKey.IdRequired",
        description: "Webhook key ID is required.");

    public static readonly Error InvalidPrefix = Error.Validation(
        code: "WebhookKey.InvalidPrefix",
        description: "Webhook key must start with a valid prefix (wk_).");

    public static readonly Error Required = Error.Validation(
        code: "WebhookKey.Required",
        description: "Webhook key is required.");

    public static readonly Error TargetUrlTooLong = Error.Validation(
        code: "WebhookKey.TargetUrlTooLong",
        description: "URL must not exceed 2000 characters.");
}
