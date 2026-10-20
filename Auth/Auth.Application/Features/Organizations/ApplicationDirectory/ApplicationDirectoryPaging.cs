using System.Globalization;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The paging and status rules of the application directory's two lists, applied by the
/// handlers AFTER <see cref="ApplicationDirectoryAccess"/> has answered "who are you": a caller
/// that cannot be served, or has no scope, learns that first, whatever its query says. So the
/// values arrive as the request carried them, and no pipeline validator runs ahead of the
/// handler for these queries.
/// </summary>
public static class ApplicationDirectoryPaging
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size accepted.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The highest page number accepted. A bound rather than "at least 1": the offset is the page
    /// number times the page size, and an unbounded page number overflows it into a server error
    /// instead of a 400. A hundred thousand pages of a hundred is ten million rows.
    /// </summary>
    public const int MaxPageNumber = 100_000;

    /// <summary>
    /// Reads the page number and size as the query carried them: absent is the default, anything
    /// else must be a whole number in range (1 to <see cref="MaxPageNumber"/>, 1 to
    /// <see cref="MaxPageSize"/>), or it is the paging code of that parameter.
    /// </summary>
    public static ErrorOr<(int Number, int Size)> Parse(string? pageNumber, string? pageSize)
    {
        var errors = new List<Error>();
        var number = Read(pageNumber, 1, MaxPageNumber, 1, PagingErrors.PageNumberOutOfRange, errors);
        var size = Read(pageSize, DefaultPageSize, MaxPageSize, 1, PagingErrors.PageSizeOutOfRange, errors);

        return errors.Count > 0 ? errors : (number, size);
    }

    /// <summary>
    /// Reads the repeatable <c>status</c> values: each one exact, case-sensitive name of
    /// <see cref="InvitationStatus"/>. A comma-joined value, another casing, a number or an empty
    /// value is <see cref="ApplicationDirectoryRefusals.StatusInvalid"/>. None at all is the
    /// default, <paramref name="defaults"/>.
    /// </summary>
    public static ErrorOr<IReadOnlyCollection<InvitationStatus>> ParseStatuses(
        IReadOnlyCollection<string>? values,
        IReadOnlyCollection<InvitationStatus> defaults)
    {
        if (values is null || values.Count == 0)
        {
            return ErrorOrFactory.From(defaults);
        }

        var names = Enum.GetNames<InvitationStatus>();
        var statuses = new List<InvitationStatus>();
        foreach (var value in values)
        {
            if (!names.Contains(value, StringComparer.Ordinal))
            {
                return ApplicationDirectoryRefusals.StatusInvalid;
            }

            statuses.Add(Enum.Parse<InvitationStatus>(value));
        }

        return ErrorOrFactory.From<IReadOnlyCollection<InvitationStatus>>(statuses);
    }

    private static int Read(string? raw, int absent, int max, int min, Error outOfRange, List<Error> errors)
    {
        if (raw is null)
        {
            return absent;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max)
        {
            return value;
        }

        errors.Add(outOfRange);
        return absent;
    }
}
