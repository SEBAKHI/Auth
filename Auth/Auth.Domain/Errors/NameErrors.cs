using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the display name of a named resource (API key, application, permission, role, webhook key, organization).
/// </summary>
public static class NameErrors
{
    public static readonly Error Required = Error.Validation(
        code: "Name.Required",
        description: "Name is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "Name.TooLong",
        description: "Name must not exceed 200 characters.");
}
