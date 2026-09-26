using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(OrganizationErrors.NewOwnerIdRequired.Code);

        RuleFor(x => x.Code)
            .Matches("^[0-9]{6}$").WithErrorCode(OrganizationErrors.TransferCodeInvalidFormat.Code)
            .When(x => !string.IsNullOrEmpty(x.Code));
    }
}
