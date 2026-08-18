using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Issues a single-use nonce for an about-to-start provider sign-in.
/// </summary>
/// <remarks>
/// The browser hands <see cref="ExternalNonceResult.Nonce"/> to the provider,
/// which seals it inside the signed ID token; the API layer stores
/// <see cref="ExternalNonceResult.CookieValue"/> in an HttpOnly cookie. At
/// sign-in the two are checked against each other, which is what makes the value
/// evidence of anything: a token minted for a different browser carries a nonce
/// that browser's cookie cannot vouch for.
/// </remarks>
public record IssueExternalNonceCommand : IRequest<ErrorOr<ExternalNonceResult>>;

/// <summary>
/// The two halves of an issued nonce.
/// </summary>
/// <param name="Nonce">The plain value, for the browser to pass to the provider.</param>
/// <param name="CookieValue">
/// The hash to store in the cookie. The plain value is never put in the cookie,
/// matching how every other opaque token here is kept.
/// </param>
public record ExternalNonceResult(string Nonce, string CookieValue);
