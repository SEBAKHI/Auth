using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetJwks;

/// <summary>
/// Handles the GetJwksQuery by retrieving the JWKS from the JWT token service.
/// </summary>
public class GetJwksQueryHandler : IRequestHandler<GetJwksQuery, ErrorOr<string>>
{
    private readonly IJwtTokenService _jwtTokenService;

    public GetJwksQueryHandler(IJwtTokenService jwtTokenService)
    {
        _jwtTokenService = jwtTokenService;
    }

    public Task<ErrorOr<string>> Handle(GetJwksQuery request, CancellationToken cancellationToken)
    {
        var jwks = _jwtTokenService.GetJwks();
        return Task.FromResult<ErrorOr<string>>(jwks);
    }
}
