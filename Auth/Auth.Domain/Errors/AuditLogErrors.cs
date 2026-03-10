using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to audit log operations.
/// </summary>
public static class AuditLogErrors
{
    public static Error NotFound(Guid auditLogId) => Error.NotFound(
        code: "AuditLog.NotFound",
        description: $"Audit log with ID '{auditLogId}' was not found.");

    public static Error InvalidDateRange => Error.Validation(
        code: "AuditLog.InvalidDateRange",
        description: "From date must be before To date.");

    public static Error DateRangeTooLarge => Error.Validation(
        code: "AuditLog.DateRangeTooLarge",
        description: "Date range cannot exceed 90 days for a single query.");

    public static Error ExportFailed(string reason) => Error.Failure(
        code: "AuditLog.ExportFailed",
        description: $"Failed to export audit logs: {reason}");

    public static Error ExportTooLarge => Error.Validation(
        code: "AuditLog.ExportTooLarge",
        description: "Export request exceeds maximum allowed records (100,000). Please narrow your search criteria.");

    public static Error InvalidExportFormat(string format) => Error.Validation(
        code: "AuditLog.InvalidExportFormat",
        description: $"Invalid export format '{format}'. Supported formats: csv, json.");

    public static Error NoLogsFound => Error.NotFound(
        code: "AuditLog.NoLogsFound",
        description: "No audit logs found matching the specified criteria.");
}
