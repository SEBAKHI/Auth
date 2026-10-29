using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.GetLoginDesignById;

/// <summary>One design in full, for its editor.</summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design.</param>
public sealed record GetLoginDesignByIdQuery(Guid? ApplicationId, Guid DesignId) : IRequest<ErrorOr<LoginDesignDto>>;

/// <summary>Handles <see cref="GetLoginDesignByIdQuery"/>.</summary>
public sealed class GetLoginDesignByIdQueryHandler : IRequestHandler<GetLoginDesignByIdQuery, ErrorOr<LoginDesignDto>>
{
    private readonly LoginDesignScope _scope;

    public GetLoginDesignByIdQueryHandler(LoginDesignScope scope)
    {
        _scope = scope;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(GetLoginDesignByIdQuery request, CancellationToken cancellationToken)
    {
        var design = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        return design.IsError ? design.Errors : LoginDesignDto.From(design.Value);
    }
}
