using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(SystemSettingsErrors.SectionKeyRequired.Code)
            .MaximumLength(64).WithErrorCode(SystemSettingsErrors.SectionKeyTooLong.Code);
    }
}
