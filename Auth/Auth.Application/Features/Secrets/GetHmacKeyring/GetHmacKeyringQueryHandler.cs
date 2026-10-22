using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GetHmacKeyring;

/// <summary>
/// Handler for reading the HMAC keyring. It reads the ring the secrets FILE holds, not
/// the configuration the process started with, which goes stale at the first change.
/// </summary>
public class GetHmacKeyringQueryHandler : IRequestHandler<GetHmacKeyringQuery, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;

    public GetHmacKeyringQueryHandler(HmacKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<HmacKeyringDto>> Handle(GetHmacKeyringQuery request, CancellationToken cancellationToken) =>
        _keyring.ViewAsync(cancellationToken);
}
