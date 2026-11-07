using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.BeginPasskeyAssertion;

/// <summary>
/// Starts a sign-in with a passkey (S20 PR B, PK5, PK6): the options the browser passes to
/// <c>navigator.credentials.get()</c>, and the challenge they carry, held for five minutes. A
/// sign-in page that asks again while its challenge has more than a minute left receives the
/// same options (T-B2).
/// </summary>
/// <param name="ChallengeHandle">The handle the challenge cookie carried, or null when there was none.</param>
/// <param name="ChallengeHandleRepeated">Whether the request carried the cookie more than once.</param>
public record BeginPasskeyAssertionCommand(string? ChallengeHandle, bool ChallengeHandleRepeated)
    : IRequest<ErrorOr<PasskeyAssertionStarted>>
{
    // The handle finds the challenge while it lives: keep it out of any log line.
    public override string ToString() =>
        $"BeginPasskeyAssertionCommand {{ ChallengeHandleRepeated = {ChallengeHandleRepeated} }}";
}

/// <summary>A sign-in started, or the live one offered again.</summary>
/// <param name="OptionsJson">The options, as the JSON the browser passes to <c>get()</c>.</param>
/// <param name="ChallengeHandle">
/// The handle of a NEW challenge, for the cookie; null when the live challenge the cookie
/// already names was offered again, and the cookie stays as it is.
/// </param>
/// <param name="ExpiresAt">When the challenge stops being accepted (UTC).</param>
public sealed record PasskeyAssertionStarted(string OptionsJson, string? ChallengeHandle, DateTime ExpiresAt)
{
    // The handle finds the challenge while it lives: keep it out of any log line.
    public override string ToString() =>
        $"PasskeyAssertionStarted {{ ExpiresAt = {ExpiresAt:O}, NewChallenge = {ChallengeHandle is not null} }}";
}
