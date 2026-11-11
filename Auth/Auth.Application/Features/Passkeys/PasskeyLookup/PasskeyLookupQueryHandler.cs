using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.PasskeyLookup;

/// <summary>
/// Answers whether an address has a live passkey of the RP ID in force (OI-280, D-S20-10).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>One read of the passkeys for every address, known or not: the repository's single
/// statement. Nothing is written, and nothing is logged here: a line per answer would be a
/// record of who holds a passkey.</item>
/// <item>False while passkeys do not run (the endpoint answers 404 then; a switch-off between
/// the gate and this read answers as an address without a passkey), and for every address
/// without a live passkey of the RP ID in force: unknown, deleted, none, all revoked, or bound
/// to another RP ID.</item>
/// <item>The account's state is not read: a locked, deactivated, unconfirmed account, or one
/// waiting for deletion, answers true when it holds a passkey. The passkey sign-in judges those
/// as it does today.</item>
/// </list>
/// </remarks>
public sealed class PasskeyLookupQueryHandler : IRequestHandler<PasskeyLookupQuery, ErrorOr<bool>>
{
    private readonly PasskeyFeature _feature;
    private readonly IUserPasskeyRepository _passkeys;

    public PasskeyLookupQueryHandler(PasskeyFeature feature, IUserPasskeyRepository passkeys)
    {
        _feature = feature;
        _passkeys = passkeys;
    }

    public async Task<ErrorOr<bool>> Handle(PasskeyLookupQuery request, CancellationToken cancellationToken)
    {
        // The gate asked the same question before the action ran; in one request the answer is
        // read once (PasskeyFeature keeps it per scope), so this costs no second read.
        var relyingParty = await _feature.RelyingPartyAsync(cancellationToken);
        if (relyingParty is null)
        {
            return false;
        }

        return await _passkeys.HasLiveForEmailAsync(request.Email, relyingParty.RpId, cancellationToken);
    }
}
