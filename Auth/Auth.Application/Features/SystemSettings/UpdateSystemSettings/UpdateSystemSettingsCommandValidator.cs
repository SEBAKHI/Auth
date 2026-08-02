using FluentValidation;

namespace Auth.Application.Features.SystemSettings.UpdateSystemSettings;

/// <summary>
/// Shape validation for the update command. The real per-field validation
/// (registry whitelist, kinds, ranges, section rules) is value-dependent and
/// lives in the handler so all field errors are reported together.
/// </summary>
public class UpdateSystemSettingsCommandValidator : AbstractValidator<UpdateSystemSettingsCommand>
{
    public UpdateSystemSettingsCommandValidator()
    {
        RuleFor(x => x.SectionKey)
            .NotEmpty().WithMessage("Validation.SectionKey.Required")
            .MaximumLength(64).WithMessage("Validation.SectionKey.MaxLength");
    }
}
