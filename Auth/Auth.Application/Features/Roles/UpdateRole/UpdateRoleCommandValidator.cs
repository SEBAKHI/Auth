using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Roles.UpdateRole;

/// <summary>
/// Validates the UpdateRoleCommand input fields.
/// </summary>
public class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
    }
}
