using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Organizations.GetPendingInvitations;

/// <summary>
/// Validates the GetPendingInvitationsQuery input fields.
/// </summary>
public class GetPendingInvitationsQueryValidator : AbstractValidator<GetPendingInvitationsQuery>
{
    public GetPendingInvitationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.OrganizationInvitations.Allowed);
    }
}
