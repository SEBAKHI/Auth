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
            .NotEmpty().WithMessage("Entity type is required.")
            .MaximumLength(100).WithMessage("Entity type must not exceed 100 characters.");
    }
}
