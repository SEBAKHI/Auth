using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.ExportAuditLogs;

/// <summary>
/// Command to export audit logs to a file.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>GetAuditLogsQuery</c>, and must keep mirroring it. Both once
/// carried ActionType and IsSuccess as parameters the repository ignored, so an
/// export narrowed by either quietly contained everything; they were removed
/// rather than left as decoration.
/// </para>
/// <para>
/// ActionType is back, and reaches the WHERE clause. It had to come back with
/// the console's category filter and not after it: the export screen sends the
/// same filters the table is showing, and a filter this command does not declare
/// is dropped by the model binder in silence. The reader would then open a file
/// they believe holds one category and find the whole table.
/// </para>
/// <para>
/// IsSuccess stays absent, because the console still has no control that sets it.
/// </para>
/// </remarks>
public record ExportAuditLogsCommand(
    string Format = "csv",
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? Action = null,
    string? ActionType = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int MaxRecords = 10000,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<ExportAuditLogsResult>>
{
    /// <summary>
    /// The ID of the user requesting the export (for audit).
    /// </summary>
    public Guid RequestedBy { get; init; }
}

/// <summary>
/// Result of an audit log export operation.
/// </summary>
public record ExportAuditLogsResult(
    byte[] Content,
    string ContentType,
    string FileName,
    int RecordCount);
