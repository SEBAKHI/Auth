using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the environment label of an API key or a webhook key.
/// </summary>
public static class EnvironmentErrors
{
    public static readonly Error Required = Error.Validation(
        code: "Environment.Required",
        description: "Environment is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "Environment.TooLong",
        description: "Environment must not exceed 50 characters.");
}
