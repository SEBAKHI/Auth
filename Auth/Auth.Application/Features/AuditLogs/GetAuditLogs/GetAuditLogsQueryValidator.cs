using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Validates the GetAuditLogsQuery input fields.
/// </summary>
public class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithMessage("To date must be after from date.")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}
