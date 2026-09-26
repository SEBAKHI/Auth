using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AuditLogs.ExportAuditLogs;

/// <summary>
/// Validates the ExportAuditLogsCommand input fields.
/// </summary>
public class ExportAuditLogsCommandValidator : AbstractValidator<ExportAuditLogsCommand>
{
    public ExportAuditLogsCommandValidator()
    {
        RuleFor(x => x.Format)
            .NotEmpty().WithErrorCode(AuditLogErrors.ExportFormatRequired.Code)
            .Must(f => f is "csv" or "json" or "excel").WithErrorCode(AuditLogErrors.ExportFormatInvalid.Code);
        RuleFor(x => x.MaxRecords)
            .InclusiveBetween(1, 10000).WithErrorCode(AuditLogErrors.ExportMaxRecordsOutOfRange.Code);
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithErrorCode(AuditLogErrors.DateRangeInvalid.Code)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);

        // Same pairing rule as the list, for a stronger reason: a file is read
        // long after the request that produced it, and the only record of what
        // it was narrowed by is what the caller sent.
        RuleFor(x => x.ParticipantRole)
            .NotNull().WithErrorCode(AuditLogErrors.ParticipantRoleRequired.Code)
            .When(x => x.ParticipantId.HasValue);
        RuleFor(x => x.ParticipantId)
            .NotNull().WithErrorCode(AuditLogErrors.ParticipantIdRequired.Code)
            .When(x => x.ParticipantRole.HasValue);
    }
}
