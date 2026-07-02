using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
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
            .NotEmpty().WithMessage("Validation.EntityType.Required")
            .MaximumLength(100).WithMessage("Validation.EntityType.MaxLength");
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);
    }
}
