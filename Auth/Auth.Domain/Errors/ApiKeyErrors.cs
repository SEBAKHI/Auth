using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to API key operations.
/// </summary>
public static class ApiKeyErrors
{
    public static Error NotFound => Error.NotFound(
        code: "ApiKey.NotFound",
        description: "The API key was not found.");

    public static Error Invalid => Error.Validation(
        code: "ApiKey.Invalid",
        description: "The provided API key is invalid.");

    public static Error Revoked => Error.Forbidden(
        code: "ApiKey.Revoked",
        description: "The API key has been revoked.");

    public static Error Expired => Error.Validation(
        code: "ApiKey.Expired",
        description: "The API key has expired.");

    public static Error AlreadyRevoked => Error.Conflict(
        code: "ApiKey.AlreadyRevoked",
        description: "The API key has already been revoked.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error InvalidPrefix = Error.Validation(
        code: "ApiKey.InvalidPrefix",
        description: "API key must start with a valid prefix (ak_).");

    public static readonly Error RateLimitPerDayNotPositive = Error.Validation(
        code: "ApiKey.RateLimitPerDayNotPositive",
        description: "Rate limit per day must be greater than 0.");

    public static readonly Error RateLimitPerMinuteNotPositive = Error.Validation(
        code: "ApiKey.RateLimitPerMinuteNotPositive",
        description: "Rate limit per minute must be greater than 0.");

    public static readonly Error Required = Error.Validation(
        code: "ApiKey.Required",
        description: "API key is required.");

    // OI-128 PR B: what a key may carry. A key with no scope authorizes nothing an application
    // can check, and a scope of another application, or of the platform, would be reported to
    // the key's application as if it were one of its own permissions.

    public static readonly Error ScopesRequired = Error.Validation(
        code: "ApiKey.ScopesRequired",
        description: "An API key must carry at least one scope.");

    public static readonly Error ScopeNotForApplication = Error.Validation(
        code: "ApiKey.ScopeNotForApplication",
        description: "Every scope of an API key must be an active permission of the key's own application.");

    public static readonly Error AllowedIpInvalid = Error.Validation(
        code: "ApiKey.AllowedIpInvalid",
        description: "Each allowed address must be an IPv4 or IPv6 address, or a CIDR range whose host bits are zero.");

    public static readonly Error AllowedIpsTooMany = Error.Validation(
        code: "ApiKey.AllowedIpsTooMany",
        description: "An API key may carry at most 50 allowed addresses.");
}
