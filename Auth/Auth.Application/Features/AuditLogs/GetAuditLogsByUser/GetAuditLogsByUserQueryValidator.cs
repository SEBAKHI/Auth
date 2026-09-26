using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
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
            .GreaterThan(x => x.FromDate).WithErrorCode(AuditLogErrors.DateRangeInvalid.Code)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);
    }
}
