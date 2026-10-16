using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.MachineKeys.CreateMachineKey;

/// <summary>
/// Validates <see cref="CreateMachineKeyCommand"/>. The key itself is checked by the key-material
/// service, which answers <c>MachineKey.InvalidPublicKey</c> for anything but a public P-256 JWK.
/// </summary>
public class CreateMachineKeyCommandValidator : AbstractValidator<CreateMachineKeyCommand>
{
    public CreateMachineKeyCommandValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(200).WithErrorCode(NameErrors.TooLong.Code)
            .When(x => x.Name is not null);

        RuleFor(x => x.Purpose)
            .IsInEnum().WithErrorCode(MachineKeyErrors.PurposeInvalid.Code);
    }
}
