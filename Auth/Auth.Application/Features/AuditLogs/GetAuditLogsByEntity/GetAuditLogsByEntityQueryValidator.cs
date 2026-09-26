using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;

/// <summary>
/// Validates the GetAuditLogsByEntityQuery input fields.
/// </summary>
public class GetAuditLogsByEntityQueryValidator : AbstractValidator<GetAuditLogsByEntityQuery>
{
    public GetAuditLogsByEntityQueryValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithErrorCode(AuditLogErrors.EntityTypeRequired.Code)
            .MaximumLength(100).WithErrorCode(AuditLogErrors.EntityTypeTooLong.Code);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);
    }
}
