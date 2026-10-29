using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Applications.UpdateApplicationAppearance;

/// <summary>
/// Validates the shape of <see cref="UpdateApplicationAppearanceCommand"/>. The appearance's own
/// choices are checked by <c>PlatformTheme.Create</c>, the platform's rules, in the handler.
/// </summary>
public class UpdateApplicationAppearanceCommandValidator : AbstractValidator<UpdateApplicationAppearanceCommand>
{
    public UpdateApplicationAppearanceCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty().WithErrorCode(ApplicationErrors.IdRequired.Code);

        // Switching it on says what to show. Switching it off may say nothing: the saved one stays.
        RuleFor(x => x.Theme)
            .NotNull().WithErrorCode(ApplicationErrors.AppearanceThemeRequired.Code)
            .When(x => x.Enabled);
    }
}
