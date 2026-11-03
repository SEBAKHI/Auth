using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GetHmacKeyring;

/// <summary>
/// Handler for reading the keyrings. It reads the rings the secrets FILE holds, not the
/// configuration the process started with, which goes stale at the first change. The HMAC
/// keyring decides the answer (S11: a 409 when it cannot be managed here); the signing keyring
/// (S12) is added to it, and says inside its own member when it cannot be managed, and why.
/// </summary>
public class GetHmacKeyringQueryHandler : IRequestHandler<GetHmacKeyringQuery, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly RsaKeyringChangeService _signingKeyring;

    public GetHmacKeyringQueryHandler(HmacKeyringChangeService keyring, RsaKeyringChangeService signingKeyring)
    {
        _keyring = keyring;
        _signingKeyring = signingKeyring;
    }

    public async Task<ErrorOr<HmacKeyringDto>> Handle(GetHmacKeyringQuery request, CancellationToken cancellationToken)
    {
        var hmac = await _keyring.ViewAsync(cancellationToken);
        if (hmac.IsError)
        {
            return hmac.Errors;
        }

        return hmac.Value with { Rsa = await _signingKeyring.ViewAsync(cancellationToken) };
    }
}
