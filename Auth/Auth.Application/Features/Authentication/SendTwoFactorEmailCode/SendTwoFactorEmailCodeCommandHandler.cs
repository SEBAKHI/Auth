using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SendTwoFactorEmailCode;

/// <summary>
/// Handler for the command that emails the code an account enters before it
/// switches on its first second factor.
/// </summary>
/// <remarks>
/// A code is minted only for an account that is setting up its first factor —
/// a pending factor row, nothing enabled — and only while the code is required.
/// The recent sign-in is checked first, as for setup itself, so a stale session
/// is asked to sign in again before anything is read or sent.
/// </remarks>
public class SendTwoFactorEmailCodeCommandHandler
    : IRequestHandler<SendTwoFactorEmailCodeCommand, ErrorOr<TwoFactorEmailCodeResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly FirstFactorEmailProof _emailProof;

    public SendTwoFactorEmailCodeCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        FirstFactorEmailProofPolicy emailProofPolicy,
        ITwoFactorStateStore twoFactorStateStore,
        FirstFactorEmailProof emailProof)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _emailProofPolicy = emailProofPolicy;
        _twoFactorStateStore = twoFactorStateStore;
        _emailProof = emailProof;
    }

    public async Task<ErrorOr<TwoFactorEmailCodeResponse>> Handle(
        SendTwoFactorEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        // A recent sign-in, before anything is read, minted or sent.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // Email off, or the switch: no code is needed, so none is minted and no
        // email goes out. The client goes on without the step.
        if (!_emailProofPolicy.IsRequired)
        {
            return new TwoFactorEmailCodeResponse(EmailCodeRequired: false, SentTo: null, ExpiresAt: null);
        }

        // The same answers enable gives for a factor in the wrong state, so a code
        // is minted only while a first factor is being set up.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot is null)
        {
            return TwoFactorErrors.SetupRequired;
        }

        if (snapshot.IsEnabled)
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        var sent = await _emailProof.SendAsync(
            request.UserId, session.Value.DeviceName, request.IpAddress, cancellationToken);
        if (sent.IsError)
        {
            return sent.Errors;
        }

        return new TwoFactorEmailCodeResponse(
            EmailCodeRequired: true,
            SentTo: sent.Value.SentTo,
            ExpiresAt: sent.Value.ExpiresAt);
    }
}
