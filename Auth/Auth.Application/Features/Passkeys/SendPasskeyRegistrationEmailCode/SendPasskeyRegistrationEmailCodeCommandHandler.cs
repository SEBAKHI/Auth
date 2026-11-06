using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;

/// <summary>
/// Handler that emails the first-factor code for a passkey registration (ARR-1049, T-A6).
/// </summary>
/// <remarks>
/// <para>
/// The same code, store and limits as the authenticator app's (X02,
/// <see cref="FirstFactorEmailProof"/>): factor-agnostic, so a code sent here is the one a
/// passkey bind checks, and sending one supersedes any earlier code of the account.
/// </para>
/// <para>
/// A code is minted only while it can be used: for a session the registration would accept
/// (<see cref="PasskeyRegistrationGate"/>, which refuses a first passkey while email is off),
/// while the bind needs it — no qualifying factor yet — and while the account holds a live
/// registration challenge. That mirrors the app's sender, which mints only while an app is
/// being set up. Otherwise it answers <c>EmailCodeRequired: false</c> and sends nothing (a
/// qualifying factor already), or <c>TwoFactor.SetupRequired</c> when no registration was
/// started.
/// </para>
/// <para>
/// The code it sends is worded for a passkey (REVIEW 150 F11): it adds a way to sign in with
/// the device's fingerprint, face or PIN, without the password.
/// </para>
/// </remarks>
public sealed class SendPasskeyRegistrationEmailCodeCommandHandler
    : IRequestHandler<SendPasskeyRegistrationEmailCodeCommand, ErrorOr<TwoFactorEmailCodeResponse>>
{
    private readonly PasskeyFeature _feature;
    private readonly PasskeyRegistrationGate _gate;
    private readonly IWebAuthnChallengeRepository _challengeRepository;
    private readonly FirstFactorEmailProof _emailProof;

    public SendPasskeyRegistrationEmailCodeCommandHandler(
        PasskeyFeature feature,
        PasskeyRegistrationGate gate,
        IWebAuthnChallengeRepository challengeRepository,
        FirstFactorEmailProof emailProof)
    {
        _feature = feature;
        _gate = gate;
        _challengeRepository = challengeRepository;
        _emailProof = emailProof;
    }

    public async Task<ErrorOr<TwoFactorEmailCodeResponse>> Handle(
        SendPasskeyRegistrationEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        if (await _feature.RelyingPartyAsync(cancellationToken) is null)
        {
            return PasskeyErrors.VerificationFailed;
        }

        var allowed = await _gate.EnsureAsync(request.UserId, request.CurrentSessionId, cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        if (!allowed.Value.EmailCodeRequired)
        {
            return new TwoFactorEmailCodeResponse(EmailCodeRequired: false, SentTo: null, ExpiresAt: null);
        }

        if (!await _challengeRepository.HasLiveRegistrationAsync(request.UserId, cancellationToken))
        {
            return TwoFactorErrors.SetupRequired;
        }

        var sent = await _emailProof.SendAsync(
            request.UserId, BindCodeFactors.Passkey, allowed.Value.Session.DeviceName, request.IpAddress, cancellationToken);
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
