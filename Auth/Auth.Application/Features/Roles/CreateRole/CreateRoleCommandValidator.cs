using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Roles.CreateRole;

/// <summary>
/// Validates the CreateRoleCommand input fields.
/// </summary>
public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Code).IsValidCode();
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
    }
}
