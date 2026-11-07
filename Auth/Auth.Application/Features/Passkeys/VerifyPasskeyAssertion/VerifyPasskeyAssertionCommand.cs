using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;

/// <summary>
/// Signs in with a passkey (S20 PR B, PK8): the browser's answer to the options of
/// <c>BeginPasskeyAssertionCommand</c>, checked against the challenge they carried. A passkey
/// the device verified the person for proves two factors on its own (M3), so no second step
/// follows.
/// </summary>
/// <param name="ChallengeHandle">The handle the challenge cookie carried, or null when there was none.</param>
/// <param name="ChallengeHandleRepeated">
/// The request carried the challenge cookie more than once (ARR-0687): refused, and nothing is
/// spent, since a page of a sibling host may have planted one.
/// </param>
/// <param name="ResponseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
/// <param name="IpAddress">The client's address.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="DeviceId">The browser's own identifier.</param>
/// <param name="StaySignedIn">Whether the person ticked "Stay signed in" (OI-154).</param>
public record VerifyPasskeyAssertionCommand(
    string? ChallengeHandle,
    bool ChallengeHandleRepeated,
    string ResponseJson,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId,
    bool StaySignedIn) : IRequest<ErrorOr<LoginResponse>>
{
    // The handle is a secret while it lives; the response names the account.
    public override string ToString() =>
        $"VerifyPasskeyAssertionCommand {{ ChallengeHandleRepeated = {ChallengeHandleRepeated}, StaySignedIn = {StaySignedIn} }}";
}
