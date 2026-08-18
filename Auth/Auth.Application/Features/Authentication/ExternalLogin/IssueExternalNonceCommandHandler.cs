using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Mints the nonce and its cookie counterpart.
/// </summary>
public class IssueExternalNonceCommandHandler
    : IRequestHandler<IssueExternalNonceCommand, ErrorOr<ExternalNonceResult>>
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _tokenKeyService;

    public IssueExternalNonceCommandHandler(
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService tokenKeyService)
    {
        _jwtTokenService = jwtTokenService;
        _tokenKeyService = tokenKeyService;
    }

    public Task<ErrorOr<ExternalNonceResult>> Handle(
        IssueExternalNonceCommand request,
        CancellationToken cancellationToken)
    {
        // The same cryptographic random source the refresh tokens and SSO session
        // tokens use; a nonce that a caller could predict would bind nothing.
        var nonce = _jwtTokenService.GenerateRefreshToken();

        return Task.FromResult<ErrorOr<ExternalNonceResult>>(
            new ExternalNonceResult(nonce, _tokenKeyService.ComputeTokenHash(nonce)));
    }
}
