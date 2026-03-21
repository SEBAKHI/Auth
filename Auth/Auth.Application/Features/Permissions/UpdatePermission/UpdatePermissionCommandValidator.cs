using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Permissions.UpdatePermission;

/// <summary>
/// Validates the UpdatePermissionCommand input fields.
/// </summary>
public class UpdatePermissionCommandValidator : AbstractValidator<UpdatePermissionCommand>
{
    public UpdatePermissionCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
    }
}
