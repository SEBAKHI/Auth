using Auth.Domain.Enums;

namespace Auth_API.Modules.AuditLogManagement.Contracts;

/// <remarks>
/// ActionType and IsSuccess were removed along with the columns that never
/// existed to back them — see <c>GetAuditLogsQuery</c>. The columns exist now,
/// and ActionType is declared again because the console filters by it: a filter
/// the console sends and this contract does not declare is dropped in silence,
/// and the resulting file is the whole table wearing the name of one category.
/// IsSuccess is declared for the same reason, now that the console filters by it.
/// </remarks>
public record ExportAuditLogsRequest(
    string Format = "csv",
    Guid? ParticipantId = null,
    AuditParticipantRole? ParticipantRole = null,
    Guid? ApplicationId = null,
    string? Action = null,
    string? ActionType = null,
    bool? IsSuccess = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int MaxRecords = 10000,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc);
