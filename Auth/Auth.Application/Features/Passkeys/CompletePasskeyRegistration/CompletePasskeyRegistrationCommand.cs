using Auth.Application.Features.Passkeys.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.CompletePasskeyRegistration;

/// <summary>
/// Completes adding a passkey (S20, PK5, PK7): the browser's answer to the options of
/// <c>BeginPasskeyRegistrationCommand</c>, checked against the challenge they carried.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="CurrentSessionId">The session the access token belongs to.</param>
/// <param name="ChallengeHandle">The handle the challenge cookie carried, or null when there was none.</param>
/// <param name="ChallengeHandleRepeated">
/// The request carried the challenge cookie more than once (ARR-0687): refused, and nothing
/// is spent, since a page of a sibling host may have planted one.
/// </param>
/// <param name="ResponseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
/// <param name="Name">The name the owner gives the passkey; blank takes a default.</param>
/// <param name="EmailCode">The code emailed for the first qualifying factor, when the bind needs one.</param>
public record CompletePasskeyRegistrationCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? ChallengeHandle,
    bool ChallengeHandleRepeated,
    string ResponseJson,
    string? Name,
    string? EmailCode) : IRequest<ErrorOr<PasskeyDto>>
{
    // The handle and the code are secrets while they live; the response names the device.
    public override string ToString() =>
        $"CompletePasskeyRegistrationCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}
