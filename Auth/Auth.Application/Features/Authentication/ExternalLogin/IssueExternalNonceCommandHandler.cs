using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Mints the nonce and its cookie counterpart.
/// </summary>
public class IssueExternalNonceCommandHandler
    : IRequestHandler<IssueExternalNonceCommand, ErrorOr<ExternalNonceResult>>
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICredentialHasher _credentialHasher;

    public IssueExternalNonceCommandHandler(
        IJwtTokenService jwtTokenService,
        ICredentialHasher credentialHasher)
    {
        _jwtTokenService = jwtTokenService;
        _credentialHasher = credentialHasher;
    }

    public Task<ErrorOr<ExternalNonceResult>> Handle(
        IssueExternalNonceCommand request,
        CancellationToken cancellationToken)
    {
        // The same cryptographic random source the refresh tokens and SSO session
        // tokens use; a nonce that a caller could predict would bind nothing.
        var nonce = _jwtTokenService.GenerateRefreshToken();

        return Task.FromResult<ErrorOr<ExternalNonceResult>>(
            new ExternalNonceResult(nonce, _credentialHasher.Hash(CredentialPurpose.ExternalNonce, nonce)));
    }
}
