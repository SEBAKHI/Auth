using Auth.Domain.Enums;

namespace Auth_API.Modules.AuditLogManagement.Contracts;

/// <remarks>
/// ActionType and IsSuccess were removed along with the columns that never
/// existed to back them — see <c>GetAuditLogsQuery</c>.
/// </remarks>
public record ExportAuditLogsRequest(
    string Format = "csv",
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int MaxRecords = 10000,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc);
