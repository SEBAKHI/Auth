using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the search parameter of a list query, shared by every list.
/// </summary>
public static class SearchErrors
{
    public static readonly Error TermTooLong = Error.Validation(
        code: "Search.TermTooLong",
        description: "Search term must not exceed 200 characters.");
}
