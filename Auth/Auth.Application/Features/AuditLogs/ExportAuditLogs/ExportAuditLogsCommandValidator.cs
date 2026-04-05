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
    }
}
