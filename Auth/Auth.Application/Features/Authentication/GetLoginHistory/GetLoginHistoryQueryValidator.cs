using FluentValidation;

namespace Auth.Application.Features.Authentication.GetLoginHistory;

/// <summary>
/// Bounds the page size the same way every other list endpoint does, so an
/// unbounded Take cannot be used to pull a user's whole history in one call.
/// </summary>
public class GetLoginHistoryQueryValidator : AbstractValidator<GetLoginHistoryQuery>
{
    public GetLoginHistoryQueryValidator()
    {
        RuleFor(x => x.Take)
            .InclusiveBetween(1, 100)
            .WithMessage("Take must be between 1 and 100.");
    }
}
