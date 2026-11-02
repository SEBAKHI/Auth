using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetAccountBar;

/// <summary>
/// What the account bar of a linked application shows (owner decisions of 2026-11-02): the
/// person the browser's single sign-on session belongs to, and the applications they can open.
/// </summary>
/// <param name="IdpSessionToken">The plain IdP session cookie value, if present.</param>
public record GetAccountBarQuery(string? IdpSessionToken) : IRequest<ErrorOr<AccountBarResult>>;

/// <summary>
/// The bar's content, or that the browser has no usable single sign-on session (the bar then
/// offers Sign in).
/// </summary>
/// <remarks>
/// "No session" is a 401 with the transport code, which no catalog error can carry; the
/// handler decides and the API layer answers, as for the organization-setup state.
/// </remarks>
public sealed record AccountBarResult(bool SignInRequired, AccountBarDto? Account)
{
    public static readonly AccountBarResult SignIn = new(true, null);
}
