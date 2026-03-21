namespace Auth_API.Modules.AuditLogManagement.Contracts;

public record ExportAuditLogsRequest(
    string Format = "csv",
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? ActionType = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool? IsSuccess = null,
    int MaxRecords = 10000);
