using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
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
            .GreaterThan(x => x.FromDate).WithMessage("Validation.DateRange.Invalid")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);
    }
}
