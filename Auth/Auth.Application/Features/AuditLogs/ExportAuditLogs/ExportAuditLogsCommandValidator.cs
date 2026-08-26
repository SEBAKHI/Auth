using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
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
            .NotEmpty().WithMessage("Validation.ExportFormat.Required")
            .Must(f => f is "csv" or "json" or "excel").WithMessage("Validation.ExportFormat.Invalid");
        RuleFor(x => x.MaxRecords)
            .InclusiveBetween(1, 10000).WithMessage("Validation.MaxRecords.Range");
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithMessage("Validation.DateRange.Invalid")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);

        // Same pairing rule as the list, for a stronger reason: a file is read
        // long after the request that produced it, and the only record of what
        // it was narrowed by is what the caller sent.
        RuleFor(x => x.ParticipantRole)
            .NotNull().WithMessage("Validation.AuditParticipant.RoleRequired")
            .When(x => x.ParticipantId.HasValue);
        RuleFor(x => x.ParticipantId)
            .NotNull().WithMessage("Validation.AuditParticipant.IdRequired")
            .When(x => x.ParticipantRole.HasValue);
    }
}
