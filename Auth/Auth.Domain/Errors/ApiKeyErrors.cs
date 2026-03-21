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
}
