using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;

/// <summary>
/// The passkey entry of the grace-period recovery (S20 PR B, D-S20-6 (a)): a passkey sign-in
/// that cancels its account's pending deletion, restores the account and signs it in. Used by
/// the person's explicit choice on the recovery page; the passkey sign-in alone never cancels
/// a deletion.
/// </summary>
/// <param name="ChallengeHandle">The handle the challenge cookie carried, or null when there was none.</param>
/// <param name="ChallengeHandleRepeated">Whether the request carried the cookie more than once.</param>
/// <param name="ResponseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
/// <param name="IpAddress">The client's address.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="DeviceId">The browser's own identifier.</param>
public record RecoverAccountWithPasskeyCommand(
    string? ChallengeHandle,
    bool ChallengeHandleRepeated,
    string ResponseJson,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId) : IRequest<ErrorOr<LoginResponse>>
{
    // The handle is a secret while it lives; the response names the account.
    public override string ToString() =>
        $"RecoverAccountWithPasskeyCommand {{ ChallengeHandleRepeated = {ChallengeHandleRepeated} }}";
}
