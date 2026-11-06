using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.RemovePasskey;

/// <summary>Removes one of the signed-in account's live passkeys (S20, PK12).</summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="PasskeyId">The passkey's row id.</param>
/// <param name="CurrentSessionId">The session the access token belongs to: it stays signed in.</param>
/// <param name="IdpSessionToken">The caller's own SSO cookie, spared with the session.</param>
public record RemovePasskeyCommand(
    Guid UserId,
    Guid PasskeyId,
    Guid? CurrentSessionId,
    string? IdpSessionToken) : IRequest<ErrorOr<Success>>
{
    // The SSO cookie is a secret while it lives.
    public override string ToString() =>
        $"RemovePasskeyCommand {{ UserId = {UserId}, PasskeyId = {PasskeyId}, CurrentSessionId = {CurrentSessionId} }}";
}
