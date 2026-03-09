using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.AuditLogs.ExportAuditLogs;

/// <summary>
/// Command to export audit logs to a file.
/// </summary>
public record ExportAuditLogsCommand(
    string Format = "csv",
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? ActionType = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool? IsSuccess = null,
    int MaxRecords = 10000) : IRequest<ErrorOr<ExportAuditLogsResult>>
{
    /// <summary>
    /// The ID of the user requesting the export (for audit).
    /// </summary>
    public Guid RequestedBy { get; set; }
}

/// <summary>
/// Result of an audit log export operation.
/// </summary>
public record ExportAuditLogsResult(
    byte[] Content,
    string ContentType,
    string FileName,
    int RecordCount);
