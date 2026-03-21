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
            .NotEmpty().WithMessage("Export format is required.")
            .Must(f => f is "csv" or "json" or "excel").WithMessage("Format must be 'csv', 'json', or 'excel'.");
        RuleFor(x => x.MaxRecords)
            .InclusiveBetween(1, 10000).WithMessage("Max records must be between 1 and 10,000.");
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithMessage("To date must be after from date.")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}
