using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// The user answering the logout confirmation: end the single sign-on session.
/// </summary>
/// <param name="IdpSessionToken">The plain SSO cookie value, if the browser holds one.</param>
public record ConfirmEndSessionCommand(string? IdpSessionToken) : IRequest<ErrorOr<Success>>;
