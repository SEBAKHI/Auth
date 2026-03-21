using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.AuditLogs.GetAuditLogsByUser;

/// <summary>
/// Validates the GetAuditLogsByUserQuery input fields.
/// </summary>
public class GetAuditLogsByUserQueryValidator : AbstractValidator<GetAuditLogsByUserQuery>
{
    public GetAuditLogsByUserQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithMessage("To date must be after from date.")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}
