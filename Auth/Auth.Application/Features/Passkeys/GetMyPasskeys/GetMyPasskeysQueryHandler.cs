using System.Buffers.Text;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.GetMyPasskeys;

/// <summary>Handler for <see cref="GetMyPasskeysQuery"/>.</summary>
/// <remarks>
/// Reads only: the list, then the handle. The handle is read whatever the list holds, so an
/// account whose last passkey was removed still lets its page tell the browser the list is now
/// empty (D-S20-7). Nothing here is logged: every value it answers is one PK16 keeps out of
/// the logs.
/// </remarks>
public sealed class GetMyPasskeysQueryHandler : IRequestHandler<GetMyPasskeysQuery, ErrorOr<PasskeyListDto>>
{
    private readonly PasskeyFeature _feature;
    private readonly IUserPasskeyRepository _passkeyRepository;

    public GetMyPasskeysQueryHandler(PasskeyFeature feature, IUserPasskeyRepository passkeyRepository)
    {
        _feature = feature;
        _passkeyRepository = passkeyRepository;
    }

    public async Task<ErrorOr<PasskeyListDto>> Handle(GetMyPasskeysQuery request, CancellationToken cancellationToken)
    {
        var live = await _passkeyRepository.GetLiveAsync(request.UserId, _feature.RpId, cancellationToken);
        var handle = await _passkeyRepository.GetUserHandleAsync(request.UserId, cancellationToken);

        return new PasskeyListDto(
            _feature.RpId,
            handle is { Length: > 0 } ? Base64Url.EncodeToString(handle) : null,
            live.Select(PasskeyDto.From).ToList());
    }
}
