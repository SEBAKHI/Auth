using FluentValidation;

namespace Auth.Application.Features.Organizations.TransferOwnership;

/// <summary>
/// Validates the TransferOwnershipCommand input fields.
/// </summary>
public class TransferOwnershipCommandValidator : AbstractValidator<TransferOwnershipCommand>
{
    public TransferOwnershipCommandValidator()
    {
        RuleFor(x => x.NewOwnerId)
            .NotEmpty().WithMessage("Validation.NewOwnerId.Required");

        RuleFor(x => x.Code)
            .Matches("^[0-9]{6}$").WithMessage("Validation.TransferCode.InvalidFormat")
            .When(x => !string.IsNullOrEmpty(x.Code));
    }
}
