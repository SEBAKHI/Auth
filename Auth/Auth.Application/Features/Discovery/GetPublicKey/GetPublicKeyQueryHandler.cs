using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetPublicKey;

/// <summary>
/// Handles the GetPublicKeyQuery by retrieving the public key PEM from the JWT token service.
/// </summary>
public class GetPublicKeyQueryHandler : IRequestHandler<GetPublicKeyQuery, ErrorOr<string>>
{
    private readonly IJwtTokenService _jwtTokenService;

    public GetPublicKeyQueryHandler(IJwtTokenService jwtTokenService)
    {
        _jwtTokenService = jwtTokenService;
    }

    public Task<ErrorOr<string>> Handle(GetPublicKeyQuery request, CancellationToken cancellationToken)
    {
        var pem = _jwtTokenService.GetPublicKeyPem();
        return Task.FromResult<ErrorOr<string>>(pem);
    }
}
