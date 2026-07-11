using FluentValidation;

namespace Auth.Application.Features.Organizations.GetInvitationByToken;

/// <summary>
/// Validates the GetInvitationByTokenQuery input.
/// </summary>
public class GetInvitationByTokenQueryValidator : AbstractValidator<GetInvitationByTokenQuery>
{
    public GetInvitationByTokenQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
    }
}
