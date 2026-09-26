using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the description of a resource.
/// </summary>
public static class DescriptionErrors
{
    public static readonly Error TooLong = Error.Validation(
        code: "Description.TooLong",
        description: "Description must not exceed 500 characters.");
}
