using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.RenamePasskey;

/// <summary>
/// Handler for <see cref="RenamePasskeyCommand"/>: only a live passkey of the caller, of the
/// current RP ID, matches; anything else is <c>Passkey.NotFound</c>, which never tells
/// another account's passkey from one that does not exist.
/// </summary>
public sealed class RenamePasskeyCommandHandler : IRequestHandler<RenamePasskeyCommand, ErrorOr<Success>>
{
    private readonly PasskeyFeature _feature;
    private readonly IUserPasskeyRepository _passkeyRepository;

    public RenamePasskeyCommandHandler(PasskeyFeature feature, IUserPasskeyRepository passkeyRepository)
    {
        _feature = feature;
        _passkeyRepository = passkeyRepository;
    }

    public async Task<ErrorOr<Success>> Handle(RenamePasskeyCommand request, CancellationToken cancellationToken)
    {
        var passkey = await _passkeyRepository.GetLiveByIdAsync(
            request.UserId, request.PasskeyId, _feature.RpId, cancellationToken);
        if (passkey is null)
        {
            return PasskeyErrors.NotFound;
        }

        var renamed = passkey.Rename(request.Name);
        if (renamed.IsError)
        {
            return renamed.Errors;
        }

        return await _passkeyRepository.TryRenameAsync(
            request.UserId, request.PasskeyId, _feature.RpId, passkey.Name, cancellationToken)
            ? Result.Success
            : PasskeyErrors.NotFound;
    }
}
