using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.GetLoginDesigns;

/// <summary>A scope's sign-in page designs: the active one first, then by name.</summary>
/// <param name="ApplicationId">The application; null for the platform's designs.</param>
public sealed record GetLoginDesignsQuery(Guid? ApplicationId) : IRequest<ErrorOr<IReadOnlyList<LoginDesignSummaryDto>>>;

/// <summary>Handles <see cref="GetLoginDesignsQuery"/>.</summary>
public sealed class GetLoginDesignsQueryHandler
    : IRequestHandler<GetLoginDesignsQuery, ErrorOr<IReadOnlyList<LoginDesignSummaryDto>>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;

    public GetLoginDesignsQueryHandler(ILoginDesignRepository designs, LoginDesignScope scope)
    {
        _designs = designs;
        _scope = scope;
    }

    public async Task<ErrorOr<IReadOnlyList<LoginDesignSummaryDto>>> Handle(
        GetLoginDesignsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await _scope.EnsureAsync(request.ApplicationId, cancellationToken);
        if (scope.IsError)
        {
            return scope.Errors;
        }

        var designs = await _designs.GetByScopeAsync(request.ApplicationId, cancellationToken);
        return designs.Select(LoginDesignSummaryDto.From).ToList();
    }
}
