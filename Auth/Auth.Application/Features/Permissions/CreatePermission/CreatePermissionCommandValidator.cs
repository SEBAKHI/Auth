using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Permissions.CreatePermission;

/// <summary>
/// Validates the CreatePermissionCommand input fields.
/// </summary>
public class CreatePermissionCommandValidator : AbstractValidator<CreatePermissionCommand>
{
    public CreatePermissionCommandValidator()
    {
        RuleFor(x => x.Code).IsValidPermissionCode();
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
    }
}
