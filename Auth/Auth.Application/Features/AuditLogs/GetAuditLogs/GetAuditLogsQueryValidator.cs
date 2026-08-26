using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Validates the GetAuditLogsQuery input fields.
/// </summary>
public class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.ToDate)
            .GreaterThan(x => x.FromDate).WithMessage("Validation.DateRange.Invalid")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.AuditLogs.Allowed);

        // The pair travels together or not at all.
        //
        // An id with no role would have to pick one, and picking the widest
        // reading turns every saved link into a broader query than the person
        // who saved it meant. A role with no id narrows nothing while looking
        // like it does — the "accepted, then quietly ignored" failure this
        // endpoint has already shipped twice.
        RuleFor(x => x.ParticipantRole)
            .NotNull().WithMessage("Validation.AuditParticipant.RoleRequired")
            .When(x => x.ParticipantId.HasValue);
        RuleFor(x => x.ParticipantId)
            .NotNull().WithMessage("Validation.AuditParticipant.IdRequired")
            .When(x => x.ParticipantRole.HasValue);
    }
}
