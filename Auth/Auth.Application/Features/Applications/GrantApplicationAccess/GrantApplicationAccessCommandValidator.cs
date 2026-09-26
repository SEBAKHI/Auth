using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Applications.GrantApplicationAccess;

/// <summary>
/// Validates the GrantApplicationAccessCommand input fields.
/// </summary>
public class GrantApplicationAccessCommandValidator : AbstractValidator<GrantApplicationAccessCommand>
{
    public GrantApplicationAccessCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty().WithErrorCode(ApplicationErrors.IdRequired.Code);
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode(UserErrors.IdRequired.Code);

        // An invitation that has already lapsed admits nobody, so accepting one
        // would only produce a row that silently does nothing.
        RuleFor(x => x.ExpiresAt!.Value)
            .GreaterThan(_ => DateTime.UtcNow).WithErrorCode(ExpiryErrors.NotInFuture.Code)
            .When(x => x.ExpiresAt.HasValue)
            // The property is ExpiresAt; without this the path would read "ExpiresAt.Value".
            .OverridePropertyName(nameof(GrantApplicationAccessCommand.ExpiresAt));

        RuleFor(x => x.Note!)
            .MaximumLength(500).WithErrorCode(ApplicationErrors.AccessNoteTooLong.Code)
            .When(x => x.Note is not null);
    }
}
