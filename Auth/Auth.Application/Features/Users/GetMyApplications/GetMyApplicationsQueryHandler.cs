using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetMyApplications;

/// <summary>
/// Handler for <see cref="GetMyApplicationsQuery"/>. The list and its rules are
/// <see cref="MyApplicationsList"/>'s, which the account bar reads too.
/// </summary>
public class GetMyApplicationsQueryHandler
    : IRequestHandler<GetMyApplicationsQuery, ErrorOr<IReadOnlyList<MyApplicationDto>>>
{
    private readonly MyApplicationsList _applications;

    public GetMyApplicationsQueryHandler(MyApplicationsList applications)
    {
        _applications = applications;
    }

    public async Task<ErrorOr<IReadOnlyList<MyApplicationDto>>> Handle(
        GetMyApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var applications = await _applications.ForUserAsync(request.UserId, cancellationToken);
        return ErrorOrFactory.From<IReadOnlyList<MyApplicationDto>>(applications);
    }
}
