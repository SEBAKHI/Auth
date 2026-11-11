using System.Text;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExchangeGoogleCode;

/// <summary>
/// Turns the one-time code Google sent back to an app's callback page into the ID token,
/// which the app then presents to <c>external-login</c> (or, for an account pending
/// deletion, to the recovery) exactly as it presented the token Google's own button gave it.
/// Signs nobody in and validates nothing: the sign-in validates the token, once.
/// </summary>
/// <param name="Code">The code from the callback address.</param>
/// <param name="CodeVerifier">The PKCE verifier the app kept for this sign-in.</param>
/// <param name="RedirectUri">
/// The app's callback address, built by the API from the verified Origin (never from the body).
/// </param>
public record ExchangeGoogleCodeCommand(
    string Code,
    string CodeVerifier,
    string RedirectUri) : IRequest<ErrorOr<GoogleIdTokenResult>>
{
    /// <summary>
    /// Names the secret members without their values: a request is written into log lines and
    /// exception messages by name, and a code with its verifier is a sign-in for a few minutes.
    /// </summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = [redacted], CodeVerifier = [redacted], RedirectUri = ").Append(RedirectUri);
        return true;
    }
}

/// <summary>The ID token Google minted, and nothing else from its answer.</summary>
/// <param name="IdToken">The ID token, unvalidated.</param>
public sealed record GoogleIdTokenResult(string IdToken)
{
    /// <summary>Names the member without its value, so no log line can carry the token.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("IdToken = [redacted]");
        return true;
    }
}
