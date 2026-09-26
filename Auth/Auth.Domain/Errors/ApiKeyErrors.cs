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
}
