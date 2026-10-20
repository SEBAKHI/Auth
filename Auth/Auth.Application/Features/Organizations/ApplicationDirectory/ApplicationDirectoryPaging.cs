using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The paging rules of the application directory's two lists.
/// </summary>
public static class ApplicationDirectoryPaging
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The highest page number accepted. A bound rather than "at least 1": the offset is the page
    /// number times the page size, and an unbounded page number overflows it into a server error
    /// instead of a 400. A hundred thousand pages of a hundred is ten million rows.
    /// </summary>
    public const int MaxPageNumber = 100_000;

    /// <summary>A page number from 1 to <see cref="MaxPageNumber"/>, with the paging code.</summary>
    public static IRuleBuilderOptions<T, int> IsDirectoryPageNumber<T>(this IRuleBuilder<T, int> ruleBuilder) =>
        ruleBuilder
            .InclusiveBetween(1, MaxPageNumber).WithErrorCode(PagingErrors.PageNumberOutOfRange.Code);

    /// <summary>A page size from 1 to 100, with the paging code.</summary>
    public static IRuleBuilderOptions<T, int> IsDirectoryPageSize<T>(this IRuleBuilder<T, int> ruleBuilder) =>
        ruleBuilder.IsValidPageSize();
}
