using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.ExportAuditLogs;

/// <summary>
/// Command to export audit logs to a file.
/// </summary>
/// <remarks>
/// Mirrors <c>GetAuditLogsQuery</c>, and shed the same two dead filters:
/// ActionType and IsSuccess reached the same repository method that ignored
/// them, so an export narrowed by either quietly contained everything.
/// </remarks>
public record ExportAuditLogsCommand(
    string Format = "csv",
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? Action = null,
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
