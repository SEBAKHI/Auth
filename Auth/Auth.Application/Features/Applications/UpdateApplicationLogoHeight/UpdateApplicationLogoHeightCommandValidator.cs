using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Applications.UpdateApplicationLogoHeight;

/// <summary>
/// Validates the shape of <see cref="UpdateApplicationLogoHeightCommand"/>. The height's range is
/// the domain's rule (<c>LogoHeight.CreateOptional</c>), checked in the handler.
/// </summary>
public class UpdateApplicationLogoHeightCommandValidator : AbstractValidator<UpdateApplicationLogoHeightCommand>
{
    public UpdateApplicationLogoHeightCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty().WithErrorCode(ApplicationErrors.IdRequired.Code);
    }
}
