using FluentValidation;

namespace Auth.Application.Features.Organizations.InitiateOwnershipTransfer;

/// <summary>
/// Validates the InitiateOwnershipTransferCommand input fields.
/// </summary>
public class InitiateOwnershipTransferCommandValidator : AbstractValidator<InitiateOwnershipTransferCommand>
{
    public InitiateOwnershipTransferCommandValidator()
    {
        RuleFor(x => x.NewOwnerId)
            .NotEmpty().WithMessage("Validation.NewOwnerId.Required");
    }
}
