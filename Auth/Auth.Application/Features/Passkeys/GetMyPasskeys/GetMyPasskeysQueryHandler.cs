using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.GetMyPasskeys;

/// <summary>Handler for <see cref="GetMyPasskeysQuery"/>.</summary>
public sealed class GetMyPasskeysQueryHandler : IRequestHandler<GetMyPasskeysQuery, ErrorOr<IReadOnlyList<PasskeyDto>>>
{
    private readonly PasskeyFeature _feature;
    private readonly IUserPasskeyRepository _passkeyRepository;

    public GetMyPasskeysQueryHandler(PasskeyFeature feature, IUserPasskeyRepository passkeyRepository)
    {
        _feature = feature;
        _passkeyRepository = passkeyRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<PasskeyDto>>> Handle(GetMyPasskeysQuery request, CancellationToken cancellationToken)
    {
        var live = await _passkeyRepository.GetLiveAsync(request.UserId, _feature.RpId, cancellationToken);
        return live.Select(PasskeyDto.From).ToList();
    }
}
