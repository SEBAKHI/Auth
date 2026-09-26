using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the sort parameter of a list query, shared by every list.
/// </summary>
public static class SortingErrors
{
    public static readonly Error SortByNotAllowed = Error.Validation(
        code: "Sorting.SortByNotAllowed",
        description: "The sort field is not allowed for this list.");
}
