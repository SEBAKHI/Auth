using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to audit log operations.
/// </summary>
public static class AuditLogErrors
{
    public static Error NotFound(Guid auditLogId) => Error.NotFound(
        code: "AuditLog.NotFound",
        description: $"Audit log with ID '{auditLogId}' was not found.",
        metadata: new() { ["args"] = new object[] { auditLogId } });

    public static Error InvalidDateRange => Error.Validation(
        code: "AuditLog.InvalidDateRange",
        description: "From date must be before To date.");

    public static Error DateRangeTooLarge => Error.Validation(
        code: "AuditLog.DateRangeTooLarge",
        description: "Date range cannot exceed 90 days for a single query.");

    public static Error ExportFailed(string reason) => Error.Failure(
        code: "AuditLog.ExportFailed",
        description: $"Failed to export audit logs: {reason}",
        metadata: new() { ["args"] = new object[] { reason } });

    public static Error ExportTooLarge => Error.Validation(
        code: "AuditLog.ExportTooLarge",
        description: "Export request exceeds maximum allowed records (100,000). Please narrow your search criteria.");

    public static Error InvalidExportFormat(string format) => Error.Validation(
        code: "AuditLog.InvalidExportFormat",
        description: $"Invalid export format '{format}'. Supported formats: csv, json.",
        metadata: new() { ["args"] = new object[] { format } });

    public static Error NoLogsFound => Error.NotFound(
        code: "AuditLog.NoLogsFound",
        description: "No audit logs found matching the specified criteria.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error DateRangeInvalid = Error.Validation(
        code: "AuditLog.DateRangeInvalid",
        description: "To date must be after from date.");

    public static readonly Error EntityTypeRequired = Error.Validation(
        code: "AuditLog.EntityTypeRequired",
        description: "Entity type is required.");

    public static readonly Error EntityTypeTooLong = Error.Validation(
        code: "AuditLog.EntityTypeTooLong",
        description: "Entity type must not exceed 100 characters.");

    public static readonly Error ExportFormatInvalid = Error.Validation(
        code: "AuditLog.ExportFormatInvalid",
        description: "Format must be 'csv', 'json', or 'excel'.");

    public static readonly Error ExportFormatRequired = Error.Validation(
        code: "AuditLog.ExportFormatRequired",
        description: "Export format is required.");

    public static readonly Error ExportMaxRecordsOutOfRange = Error.Validation(
        code: "AuditLog.ExportMaxRecordsOutOfRange",
        description: "Max records must be between 1 and 10,000.");

    public static readonly Error ParticipantIdRequired = Error.Validation(
        code: "AuditLog.ParticipantIdRequired",
        description: "A participant role needs the person it applies to.");

    public static readonly Error ParticipantRoleRequired = Error.Validation(
        code: "AuditLog.ParticipantRoleRequired",
        description: "A participant filter needs a role: subject, actor, or either.");
}
