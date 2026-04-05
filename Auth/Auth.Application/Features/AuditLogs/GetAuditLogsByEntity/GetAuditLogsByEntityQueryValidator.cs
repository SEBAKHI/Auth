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
    }
}
