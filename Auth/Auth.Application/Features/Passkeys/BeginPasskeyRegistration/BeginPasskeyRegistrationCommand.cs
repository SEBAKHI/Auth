using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.BeginPasskeyRegistration;

/// <summary>
/// Starts adding a passkey to the signed-in account (S20, PK5): the options the browser
/// passes to <c>navigator.credentials.create()</c>, and the challenge they carry, held for
/// five minutes.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="CurrentSessionId">The session the access token belongs to.</param>
public record BeginPasskeyRegistrationCommand(Guid UserId, Guid? CurrentSessionId)
    : IRequest<ErrorOr<PasskeyRegistrationStarted>>;

/// <summary>A registration started.</summary>
/// <param name="OptionsJson">The options, as the JSON the browser passes to <c>create()</c>.</param>
/// <param name="ChallengeHandle">The handle of the challenge: carried by the cookie, never in the body.</param>
/// <param name="ExpiresAt">When the challenge stops being accepted (UTC).</param>
/// <param name="EmailCodeRequired">Whether completing it needs the code emailed to the account.</param>
public sealed record PasskeyRegistrationStarted(
    string OptionsJson,
    string ChallengeHandle,
    DateTime ExpiresAt,
    bool EmailCodeRequired)
{
    // The handle finds the challenge while it lives: keep it out of any log line.
    public override string ToString() =>
        $"PasskeyRegistrationStarted {{ ExpiresAt = {ExpiresAt:O}, EmailCodeRequired = {EmailCodeRequired} }}";
}
