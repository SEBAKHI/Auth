using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the paging parameters of a list query, shared by every list.
/// </summary>
public static class PagingErrors
{
    public static readonly Error PageNumberOutOfRange = Error.Validation(
        code: "Paging.PageNumberOutOfRange",
        description: "Page number must be at least 1.");

    public static readonly Error PageSizeOutOfRange = Error.Validation(
        code: "Paging.PageSizeOutOfRange",
        description: "Page size must be between 1 and 100.");

    public static readonly Error TakeOutOfRange = Error.Validation(
        code: "Paging.TakeOutOfRange",
        description: "The number of entries must be between 1 and 100.");
}
